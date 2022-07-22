using FuzzySharp;
using Manganese.Text;
using Spectre.Console;
using SpotifyAPI.Web;
using SpotifyAPI.Web.Auth;
using Swan.Logging;

namespace SpotifyCreater;

public static class SpotifyService
{
    private static SpotifyClient? _spotifyClient;
    private static AuthorizationCodeTokenResponse? _tokenResponse;

    public static async Task InitializeAsync()
    {
        if (_tokenResponse?.IsExpired is true)
        {
            var newResponse = await new OAuthClient().RequestToken(
                new AuthorizationCodeRefreshRequest(Credentiality.ClientId, Credentiality.ClientSecret,
                    _tokenResponse.RefreshToken)
            );

            _spotifyClient = new SpotifyClient(newResponse.AccessToken);
        }
        if (_spotifyClient == null)
        {
            _spotifyClient = await GetSpotifyClientAsync();
        }
    }
    
    public static async Task<SpotifyClient> GetSpotifyClientAsync()
    {
        var server = new EmbedIOAuthServer(new Uri("http://localhost:5000/callback"), 5000);
        await server.Start();

        "Stared server...".Log();
        
        SpotifyClient spotifyClient = null!;
        server.AuthorizationCodeReceived += async (_, response) =>
        {
            await server.Stop();

            var config = SpotifyClientConfig.CreateDefault();
            _tokenResponse = await new OAuthClient(config).RequestToken(
                new AuthorizationCodeTokenRequest(
                    Credentiality.ClientId, Credentiality.ClientSecret, response.Code, new Uri("http://localhost:5000/callback")
                )
            );

            spotifyClient = new SpotifyClient(_tokenResponse.AccessToken.Log("Access token is: "));
        };

        server.ErrorReceived += async (_, error, _) =>
        {
            $"Aborting authorization, error received: {error}".Error();
            await server.Stop();
        };
        
        var request = new LoginRequest(server.BaseUri, Credentiality.ClientId, LoginRequest.ResponseType.Code)
        {
            Scope = new List<string>
            {
                Scopes.PlaylistModifyPublic, Scopes.PlaylistModifyPrivate, Scopes.PlaylistReadPrivate,
                Scopes.PlaylistReadCollaborative
            }
        };
        BrowserUtil.Open(request.ToUri().Log("Opening uri: "));

        while (spotifyClient == null) { }

        "SpotifyClient object initialized".Log();
        
        return spotifyClient;
    }
    
    public static async Task<FullAlbum?> GetAlbumAsync(string albumName, string artistName)
    {
        await InitializeAsync(); 

        $"Searching for [green]{albumName}[/]".Log();

        var searchResponse =
            (await _spotifyClient!.Search.Item(new SearchRequest(SearchRequest.Types.Album, $"{albumName} {artistName}")))
            .Log("Total albums found: ", response => $"[green]{response.Albums.Total}[/]");
        
        if (searchResponse.Albums.Items == null)
        {
            throw new InvalidOperationException("Failed to search albums");
        }

        var candidates = searchResponse.Albums.Items
            .Where(simpleAlbum =>
                simpleAlbum.Artists.Any(a => Fuzz.Ratio(a.Name, artistName) >= 80) &&
                simpleAlbum.Name.ToLower().StartsWith(albumName.ToLower()))
            .ToList()
            .Log("Candidates count: ", list => $"[green]{list.Count}[/]");

        if (candidates.Count == 0)
        {
            var similarAlbum =
                searchResponse.Albums.Items.FirstOrDefault(x => Fuzz.Ratio(x.Name, albumName) >= 80, null);
            if (similarAlbum != null)
            {
                AnsiConsole.MarkupLine($"[yellow]Found:[/] {FormatAlbum(similarAlbum)}");
                if (AnsiConsole.Confirm("There's an very similar album with expected one, include it?"))
                {
                    return await _spotifyClient.Albums.Get(similarAlbum.Id);
                }
            }
            $"Cannot found anything about  album [yellow]{albumName.EscapeMarkup()}[/]".Warn();
            return null;
        }
        if (candidates.Count == 1)
        {
            return await _spotifyClient.Albums.Get(candidates.First().Id);
        }

        var formattedCandidates = candidates
            .Select(x => ($"{x.Name}({x.ReleaseDate}) - {x.Artists.Select(x => x.Name).JoinToString(",")}", x.Id)).ToList();
        var selected = AnsiConsole.Prompt(new SelectionPrompt<string>()
            .Title("Press [green]enter[/] to choose [green]one[/]: ")
            .MoreChoicesText("[grey](Move up and down to reveal more candidates)[/]")
            .AddChoices(formattedCandidates.Select(x => x.Item1)));

        var selectedCandidate = formattedCandidates.First(x => x.Item1 == selected);

        return await _spotifyClient.Albums.Get(selectedCandidate.Id);
    }

    private static (string, string) FormatAlbum(SimpleAlbum album)
    {
        return ($"{album.Name}({album.ReleaseDate}) - {album.Artists.Select(album => album.Name).JoinToString(",")}",
            album.Id);
    }
    
    public static async Task<FullPlaylist> CreatePlaylistAsync(string playlistName)
    {
        await InitializeAsync(); 

        var currentUser =
            (await _spotifyClient!.UserProfile.Current()).Log("Current user is: ", user => $"[green]{user.DisplayName.EscapeMarkup()}[/]");
        
        return await _spotifyClient.Playlists.Create(currentUser.Id, new PlaylistCreateRequest(playlistName));
    }

    public static async Task PopulatePlaylistAsync(string playlistId, FullAlbum toPopulate)
    {
        await InitializeAsync(); 

        var playlist = await _spotifyClient!.Playlists.Get(playlistId);
        
        $"Populating album [green]{toPopulate.Name.EscapeMarkup()}[/] to playlist [green]{playlist.Name.EscapeMarkup()}[/]".Log();
        
        var tracks = toPopulate.Tracks.Items!
            .Log("Tracks to be added: ", list => $"[green]{list.Select(x => x.Name).JoinToString(",").EscapeMarkup()}[/]")
            .Select(t => t.Uri)
            .ToList();
        
        await _spotifyClient.Playlists.AddItems(playlist.Id!, new PlaylistAddItemsRequest(tracks));
    }
}