using System.Text.Json;

namespace PocketViewport;

internal sealed class RecentSites
{
    public const int Capacity = 5;
    private readonly string file;
    private IReadOnlyList<Uri> current;

    public RecentSites(string profileFolder)
    {
        file = Path.Combine(profileFolder, "recent-sites.json");
        current = Read() ?? [];
    }

    public IReadOnlyList<Uri> Entries => current;

    public void Record(Uri uri)
    {
        if (uri.UserInfo.Length > 0)
            return;
        using var gate = new Mutex(false, @"Local\PocketViewport.RecentSites");
        bool locked;
        try
        {
            locked = gate.WaitOne(TimeSpan.FromMilliseconds(250));
        }
        catch (AbandonedMutexException)
        {
            locked = true;
        }
        try
        {
            var existing = locked ? Read() ?? current : current;
            current = existing.Where(site => site.AbsoluteUri != uri.AbsoluteUri)
                .Prepend(uri)
                .Take(Capacity)
                .ToArray();
            if (locked)
                Write();
        }
        finally
        {
            if (locked)
                gate.ReleaseMutex();
        }
    }

    private IReadOnlyList<Uri>? Read()
    {
        try
        {
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read,
                FileShare.Read | FileShare.Delete);
            var addresses = JsonSerializer.Deserialize<string[]>(stream) ?? [];
            var recent = new List<Uri>(Capacity);
            foreach (var address in addresses)
            {
                if (PhoneWindow.TryWebUri(address, out var uri) && uri.UserInfo.Length == 0 &&
                    !recent.Any(site => site.AbsoluteUri == uri.AbsoluteUri))
                    recent.Add(uri);
                if (recent.Count == Capacity)
                    break;
            }
            return recent;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private void Write()
    {
        try
        {
            string temporary = file + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(current.Select(site => site.AbsoluteUri)));
            File.Move(temporary, file, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Keep the in-memory list available if the profile cannot be written.
        }
    }
}
