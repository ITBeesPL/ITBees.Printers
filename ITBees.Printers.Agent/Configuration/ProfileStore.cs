using System.Text.Json;

namespace ITBees.Printers.Agent.Configuration;

/// <summary>The services this agent knows, kept in profiles.json of the per-user data directory.</summary>
public class ProfileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly object _sync = new();
    private readonly string _path = Path.Combine(AgentInfo.DataDirectory, "profiles.json");
    private List<ServiceProfile> _profiles = new();

    public void Load()
    {
        lock (_sync)
        {
            if (!File.Exists(_path))
            {
                return;
            }

            try
            {
                _profiles = JsonSerializer.Deserialize<List<ServiceProfile>>(File.ReadAllText(_path)) ?? new();
            }
            catch (JsonException)
            {
                // A damaged file is not worth crashing over - the services just need a new login.
                _profiles = new();
            }
        }
    }

    public List<ServiceProfile> GetAll()
    {
        lock (_sync)
        {
            return _profiles.ToList();
        }
    }

    public ServiceProfile? Find(string siteUrl)
    {
        var key = NormalizeSiteUrl(siteUrl);
        lock (_sync)
        {
            return _profiles.FirstOrDefault(x => NormalizeSiteUrl(x.SiteUrl) == key);
        }
    }

    public void Save(ServiceProfile profile)
    {
        var key = NormalizeSiteUrl(profile.SiteUrl);
        lock (_sync)
        {
            _profiles.RemoveAll(x => !ReferenceEquals(x, profile) && NormalizeSiteUrl(x.SiteUrl) == key);
            if (!_profiles.Contains(profile))
            {
                _profiles.Add(profile);
            }

            Write();
        }
    }

    public void Remove(ServiceProfile profile)
    {
        lock (_sync)
        {
            _profiles.Remove(profile);
            Write();
        }
    }

    /// <summary>"https://Admin.Example.com/" and "https://admin.example.com" are the same service.</summary>
    public static string NormalizeSiteUrl(string siteUrl)
    {
        return (siteUrl ?? string.Empty).Trim().TrimEnd('/').ToLowerInvariant();
    }

    private void Write()
    {
        // Write-then-rename, so a crash in the middle never leaves a half-written file behind.
        var temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(_profiles, JsonOptions));
        File.Move(temp, _path, overwrite: true);
    }
}
