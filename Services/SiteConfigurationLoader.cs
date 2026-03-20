using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Pascal.Edge.WebServiceAgent.Models;

namespace Pascal.Edge.WebServiceAgent.Services;

public class SiteConfigurationLoader : IOptionsMonitor<SiteOptions>, IDisposable
{
    private readonly string _configPath;
    private readonly FileSystemWatcher _watcher;
    
    private SiteOptions _currentOptions;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SiteConfigurationLoader> _logger;
    private readonly List<Action<SiteOptions, string?>> _changeListeners = new();
    private readonly object _lock = new();
    private readonly IChangeToken _reloadToken;

    public SiteConfigurationLoader(
        IConfiguration configuration,
        ILogger<SiteConfigurationLoader> logger)
    {
        _configuration = configuration;
        _logger = logger;
        
        var basePath = AppContext.BaseDirectory;
        _configPath = Path.Combine(basePath, "appsettings.json");
        
        _currentOptions = LoadConfiguration();
        
        var directory = Path.GetDirectoryName(_configPath) ?? ".";
        var fileName = Path.GetFileName(_configPath);
        
        _watcher = new FileSystemWatcher(directory, fileName)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size
        };
        
        _watcher.Changed += OnConfigFileChanged;
        _watcher.EnableRaisingEvents = true;
        
        _reloadToken = _configuration.GetReloadToken();
    }

    public SiteOptions CurrentValue => _currentOptions;
    
    public SiteOptions Get(string? name) => _currentOptions;
    
    public IDisposable OnChange(Action<SiteOptions, string?> listener)
    {
        lock (_lock)
        {
            _changeListeners.Add(listener);
        }
        
        return new ChangeRegistration(() =>
        {
            lock (_lock)
            {
                _changeListeners.Remove(listener);
            }
        });
    }

    public IChangeToken GetReloadToken() => _reloadToken;

    private void OnConfigFileChanged(object sender, FileSystemEventArgs e)
    {
        try
        {
            Thread.Sleep(100);
            ReloadConfiguration();
            _logger.LogInformation("配置文件已重新加载");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "重新加载配置文件失败");
        }
    }

    private void ReloadConfiguration()
    {
        var newOptions = LoadConfiguration();
        
        lock (_lock)
        {
            _currentOptions = newOptions;
        }
        
        foreach (var listener in _changeListeners.ToList())
        {
            try
            {
                listener(newOptions, null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "执行配置变更回调失败");
            }
        }
    }

    private SiteOptions LoadConfiguration()
    {
        try
        {
            if (!File.Exists(_configPath))
            {
                _logger.LogWarning("配置文件不存在: {ConfigPath}", _configPath);
                return new SiteOptions();
            }
            
            var json = File.ReadAllText(_configPath);
            var options = JsonSerializer.Deserialize<SiteOptions>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            
            if (options?.Sites == null)
            {
                options ??= new SiteOptions();
            }
            
            return options;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "加载配置文件失败");
            return new SiteOptions();
        }
    }

    public Site? GetSiteByHostname(string hostname)
    {
        var options = _currentOptions;
        
        foreach (var site in options.Sites)
        {
            foreach (var h in site.Hostnames)
            {
                if (string.Equals(h, hostname, StringComparison.OrdinalIgnoreCase))
                {
                    return site;
                }
            }
        }
        
        return null;
    }
    
    public Site? GetSiteByPort(int port)
    {
        var options = _currentOptions;
        
        var site = options.Sites.FirstOrDefault(s => s.Port == port);
        if (site != null)
        {
            return site;
        }
        
        if (!string.IsNullOrEmpty(options.DefaultSite))
        {
            return options.Sites.FirstOrDefault(s => 
                string.Equals(s.Name, options.DefaultSite, StringComparison.OrdinalIgnoreCase));
        }
        
        return options.Sites.FirstOrDefault();
    }

    public void Dispose()
    {
        _watcher.EnableRaisingEvents = false;
        _watcher.Dispose();
    }

    private class ChangeRegistration : IDisposable
    {
        private readonly Action _action;
        private bool _disposed;

        public ChangeRegistration(Action action)
        {
            _action = action;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                _action();
            }
        }
    }
}
