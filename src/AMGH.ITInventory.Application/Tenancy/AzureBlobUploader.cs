using System.Globalization;
using System.Net.Http.Headers;
using System.Text;

namespace AMGH.ITInventory.Application.Tenancy;

/// <summary>Uploads a file to an Azure Blob container with a container SAS URL, in 8 MB blocks (no package needed; works for files of any size up to 50,000 blocks).</summary>
internal static class AzureBlobUploader
{
    private const int BlockSize = 8 * 1024 * 1024;
    private const string ApiVersion = "2021-08-06";

    internal static async Task UploadAsync(string containerSasUrl, string blobName, Stream content, HttpMessageHandler? handler, CancellationToken ct)
    {
        if (!Uri.TryCreate(containerSasUrl, UriKind.Absolute, out var container) || container.Scheme != Uri.UriSchemeHttps && !container.IsLoopback)
            throw new InvalidOperationException("Tenancy:OffsiteBlobUrl must be an https container SAS URL.");
        if (string.IsNullOrEmpty(container.Query)) throw new InvalidOperationException("Tenancy:OffsiteBlobUrl needs the SAS token (?sv=...&sig=...).");
        var basePath = container.AbsolutePath.TrimEnd('/');
        string BlobUri(string extra) => $"{container.Scheme}://{container.Authority}{basePath}/{Uri.EscapeDataString(blobName)}{container.Query}{extra}";

        using var http = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        http.Timeout = TimeSpan.FromMinutes(30);
        var ids = new List<string>();
        var buffer = new byte[BlockSize];
        while (true)
        {
            var read = 0;
            while (read < buffer.Length)
            {
                var n = await content.ReadAsync(buffer.AsMemory(read), ct);
                if (n == 0) break;
                read += n;
            }
            if (read == 0 && ids.Count > 0) break;
            var id = Convert.ToBase64String(Encoding.ASCII.GetBytes(ids.Count.ToString("D6", CultureInfo.InvariantCulture)));
            ids.Add(id);
            using var put = new HttpRequestMessage(HttpMethod.Put, BlobUri("&comp=block&blockid=" + Uri.EscapeDataString(id))) { Content = new ByteArrayContent(buffer, 0, read) };
            put.Headers.Add("x-ms-version", ApiVersion);
            put.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            await EnsureAsync(await http.SendAsync(put, ct), "upload a block");
            if (read < buffer.Length) break;
        }
        var xml = new StringBuilder("<?xml version=\"1.0\" encoding=\"utf-8\"?><BlockList>");
        foreach (var id in ids) xml.Append("<Latest>").Append(id).Append("</Latest>");
        xml.Append("</BlockList>");
        using var commit = new HttpRequestMessage(HttpMethod.Put, BlobUri("&comp=blocklist")) { Content = new StringContent(xml.ToString(), Encoding.UTF8, "application/xml") };
        commit.Headers.Add("x-ms-version", ApiVersion);
        await EnsureAsync(await http.SendAsync(commit, ct), "finish the upload");
    }

    private static async Task EnsureAsync(HttpResponseMessage response, string what)
    {
        using (response)
        {
            if (response.IsSuccessStatusCode) return;
            var body = await response.Content.ReadAsStringAsync();
            var code = body.Contains("<Code>", StringComparison.Ordinal) ? body.Split("<Code>")[1].Split("</Code>")[0] : response.ReasonPhrase;
            throw new InvalidOperationException($"Azure refused to {what}: HTTP {(int)response.StatusCode} {code}.");
        }
    }
}
