namespace Pascal.Edge.WebServiceAgent.Models;

public class Site
{
    public string Name { get; set; } = string.Empty;
    public int Port { get; set; }
    public string[] Hostnames { get; set; } = Array.Empty<string>();
    public string Path { get; set; } = string.Empty;
    public string? ForwardUrl { get; set; }
}

public class SiteOptions
{
    public string DefaultDocument { get; set; } = "index.html";
    public bool EnableSPAFallback { get; set; } = true;
    public string? DefaultSite { get; set; }
    public List<Site> Sites { get; set; } = new();
}
