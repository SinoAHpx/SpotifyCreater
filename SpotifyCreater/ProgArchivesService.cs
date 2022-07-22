using Flurl.Http;
using HtmlAgilityPack;
using Manganese.Array;
using MoreLinq.Extensions;

namespace SpotifyCreater;

public static class ProgArchivesService
{
    private static readonly FileInfo HtmlCache =
        new FileInfo(@$"{Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)}\cache.html");

    private static async Task<string> GetHtmlTextAsync()
    {
        string htmlText;

        if (HtmlCache.Exists)
        {
            htmlText = await File.ReadAllTextAsync(HtmlCache.FullName);
        }
        else
        {
            htmlText = await "http://www.progarchives.com/top-prog-albums.asp?salbumtypes=1".GetStringAsync();
            await File.WriteAllTextAsync(HtmlCache.FullName, htmlText);
        }

        return htmlText;
    }

    public static async Task<List<AlbumMetadata>> GetAlbumMetadataAsync()
    {
        var htmlDoc = new HtmlDocument();
        var htmlText = await GetHtmlTextAsync();
        htmlDoc.LoadHtml(htmlText);
        
        var linkNodes = htmlDoc.DocumentNode.SelectNodes("//a");
        var albums = new List<string>();

        foreach (var linkNode in linkNodes)
        {
            var href = linkNode.Attributes["href"];
            if (href == null)
                continue;
            if (linkNode.InnerText.Contains("Shop") || linkNode.InnerText.Contains("VA"))
                continue;


            var value = href.Value;
            if (value.Contains("album.asp?id="))
            {
                albums.Add(linkNode.InnerText.Trim());
            }

            if (value.Contains("artist.asp?id="))
            {
                albums.Add(linkNode.InnerText.Trim());
            }
        }

        var albumsMeta = albums
            .Batch(2)
            .Select(x => x.ToList())
            .Select(x => new AlbumMetadata(x[0], x[1]))
            .ToList();

        return albumsMeta;
    }
}

public class AlbumMetadata
{
    public AlbumMetadata(string albumName, string artistName)
    {
        AlbumName = albumName;
        ArtistName = artistName;
    }

    public string AlbumName { get; set; }

    public string ArtistName { get; set; }
}