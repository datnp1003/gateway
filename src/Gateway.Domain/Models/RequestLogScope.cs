namespace Gateway.Domain.Models;

public static class RequestLogScope
{
    private static readonly string[] InternalPrefixes = ["/api/auth", "/signin-google", "/api/management", "/management", "/health", "/healthz", "/assets", "/static", "/dashboard", "/favicon.svg", "/favicon.ico", "/index.html", "/@vite", "/src"];

    public static bool IsInternal(string path) => path == "/" || InternalPrefixes.Any(prefix =>
        path.Equals(prefix, StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase));
}
