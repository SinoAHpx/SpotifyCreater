using FuzzySharp;
using Polly;
using Spectre.Console;
using SpotifyCreater;

await SpotifyService.InitializeAsync();

var playlistName = AnsiConsole.Ask<string>("Input name of the [green]new playlist[/]: ");

var playlist = await SpotifyService.CreatePlaylistAsync(playlistName!);

var albums = await ProgArchivesService.GetAlbumMetadataAsync();

foreach (var albumMetadata in albums)
{
    await Policy
        .Handle<Exception>()
        .RetryAsync(3, (exception, span) =>
        {
            AnsiConsole.WriteException(exception);
        })
        .ExecuteAsync(async () =>
        {
            var album = await SpotifyService.GetAlbumAsync(albumMetadata.AlbumName, albumMetadata.ArtistName);
            if (album == null)
            {
                $"Album [green]{albumMetadata.AlbumName}[/] is not available on spotify, [red]skipped[/]".Warn();
                return;
            }
            
            await SpotifyService.PopulatePlaylistAsync(playlist.Id!, album);

            $"Done for album: [green]{album.Name}[/]".Log();
        });
}