using Eidolon.Core.Application;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Eidolon.Core.Domain;

namespace Eidolon.Core.Infrastructure
{
    public class FileDownloader
    {
        private readonly HttpClient _http;
        private readonly TimeProvider _time;

        public FileDownloader(HttpClient http, TimeProvider time)
        {
            _http = http;
            _time = time;
        }

        public async Task DownloadAsync(string url, string destination, string expectedChecksum,
            IProgress<WorkProgress> progress, CancellationToken cancellationToken)
        {
            if (File.Exists(destination) == true)
            {
                await VerifyAsync(destination, expectedChecksum, cancellationToken).ConfigureAwait(false);
                return;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            string partial = destination + ".part";
            long offset = 0;
            if (File.Exists(partial) == true)
            {
                offset = new FileInfo(partial).Length;
            }
            using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, url);
            if (offset > 0)
            {
                request.Headers.Range = new RangeHeaderValue(offset, null);
            }
            using HttpResponseMessage response = await _http.SendAsync(request,
                HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable && offset > 0)
            {
                if (string.IsNullOrEmpty(expectedChecksum) == true)
                {
                    throw new StudioException(StudioMessageCode.IncompleteDownload);
                }
                try
                {
                    await VerifyAsync(partial, expectedChecksum, cancellationToken).ConfigureAwait(false);
                }
                catch (StudioException error) when (error.Code == StudioMessageCode.DownloadChecksumMismatch)
                {
                    File.Delete(partial);
                    throw;
                }
                File.Move(partial, destination);
                return;
            }
            response.EnsureSuccessStatusCode();
            FileMode mode = FileMode.Create;
            if (response.StatusCode == HttpStatusCode.PartialContent)
            {
                if (response.Content.Headers.ContentRange?.From != offset)
                {
                    throw new StudioException(StudioMessageCode.InvalidDownloadRange);
                }
                mode = FileMode.Append;
            }
            else
            {
                offset = 0;
            }
            long totalBytes = offset;
            if (response.Content.Headers.ContentLength.HasValue == true)
            {
                totalBytes += response.Content.Headers.ContentLength.Value;
            }
            long received = offset;
            DateTimeOffset lastReportAtUtc = _time.GetUtcNow();
            await using (Stream input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (FileStream output = new FileStream(partial, mode, FileAccess.Write, FileShare.None,
                1024 * 1024, FileOptions.Asynchronous))
            {
                byte[] buffer = new byte[1024 * 1024];
                int count;
                while ((count = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
                    received += count;
                    DateTimeOffset now = _time.GetUtcNow();
                    if ((now - lastReportAtUtc).TotalMilliseconds >= 300)
                    {
                        double percent = 0;
                        bool indeterminate = true;
                        if (totalBytes > 0)
                        {
                            percent = 100.0 * received / totalBytes;
                            indeterminate = false;
                        }
                        progress.Report(new WorkProgress(StudioMessageCode.DownloadProgress, percent, indeterminate, Path.GetFileName(destination), received / 1048576));
                        lastReportAtUtc = now;
                    }
                }
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            if (totalBytes > 0 && received != totalBytes)
            {
                throw new StudioException(StudioMessageCode.DownloadInterrupted);
            }
            progress.Report(new WorkProgress(StudioMessageCode.VerifyingDownload));
            try
            {
                await VerifyAsync(partial, expectedChecksum, cancellationToken).ConfigureAwait(false);
            }
            catch (StudioException error) when (error.Code == StudioMessageCode.DownloadChecksumMismatch)
            {
                File.Delete(partial);
                throw;
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(partial, destination);
        }

        private async Task VerifyAsync(string path, string expectedChecksum, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(expectedChecksum) == true)
            {
                return;
            }
            await using FileStream stream = File.OpenRead(path);
            byte[] hash;
            if (expectedChecksum.StartsWith("md5:", StringComparison.OrdinalIgnoreCase) == true)
            {
                hash = await MD5.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
                expectedChecksum = expectedChecksum.Substring(4);
            }
            else
            {
                hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
            }
            if (Convert.ToHexString(hash).Equals(expectedChecksum, StringComparison.OrdinalIgnoreCase) == false)
            {
                throw new StudioException(StudioMessageCode.DownloadChecksumMismatch, Path.GetFileName(path));
            }
        }
    }
}
