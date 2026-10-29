using System.Net;
using System.Text.RegularExpressions;

namespace Services.IntegrationTests;

/// <summary>Completes Keycloak's login form the way a browser would.</summary>
internal static partial class KeycloakLogin
{
    /// <summary>
    /// Opens the login page the gateway redirected to, submits the credentials and returns where
    /// Keycloak sends the browser next: the gateway's /signin-oidc callback with the code.
    /// </summary>
    public static async Task<Uri> SignInAsync(string realmUrl, Uri loginUrl, string username, string password, CancellationToken ct)
    {
        using var keycloak = new HttpClient(new BrowserCookieJar());
        var loginPage = await OpenPageAsync(keycloak, realmUrl, loginUrl, ct);
        var formAction = WebUtility.HtmlDecode(LoginFormAction().Match(loginPage).Groups["action"].Value);

        var login = await keycloak.PostAsync(formAction, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["username"] = username,
            ["password"] = password,
        }), ct);

        Assert.Equal(HttpStatusCode.Found, login.StatusCode);
        return login.Headers.Location!;
    }

    /// <summary>
    /// Follows Keycloak's own redirects (it may hop through login-actions URLs) like a browser would,
    /// and fails with the target if Keycloak sends the browser anywhere else, e.g. back with an error.
    /// </summary>
    private static async Task<string> OpenPageAsync(HttpClient keycloak, string realmUrl, Uri url, CancellationToken ct)
    {
        for (var hop = 0; hop < 5; hop++)
        {
            using var response = await keycloak.GetAsync(url, ct);
            if (response.IsSuccessStatusCode) return await response.Content.ReadAsStringAsync(ct);

            var location = response.Headers.Location;
            if (location is null || !new Uri(url, location).ToString().StartsWith(realmUrl))
                Assert.Fail($"Keycloak answered {(int)response.StatusCode} for {url}; Location: {location}; body: {await response.Content.ReadAsStringAsync(ct)}");

            url = new Uri(url, location);
        }

        Assert.Fail("Too many redirects inside Keycloak.");
        return "";
    }

    [GeneratedRegex("<form[^>]*action=\"(?<action>[^\"]+)\"")]
    private static partial Regex LoginFormAction();

    /// <summary>
    /// Keycloak marks its session cookies <c>Secure</c>. Browsers still send them to http://localhost
    /// (a secure context), but <see cref="CookieContainer"/> won't over HTTP, so this jar keeps
    /// cookies by name and sends them back regardless of the Secure flag, like a browser on localhost.
    /// </summary>
    private sealed class BrowserCookieJar() : DelegatingHandler(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
    {
        private readonly Dictionary<string, string> _cookies = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (_cookies.Count > 0)
                request.Headers.Add("Cookie", string.Join("; ", _cookies.Select(c => $"{c.Key}={c.Value}")));

            var response = await base.SendAsync(request, cancellationToken);

            foreach (var header in response.Headers.TryGetValues("Set-Cookie", out var values) ? values : [])
            {
                var pair = header.Split(';', 2)[0];
                var name = pair.Split('=', 2)[0].Trim();
                var value = pair.Contains('=') ? pair.Split('=', 2)[1] : "";
                var expired = header.Contains("Max-Age=0", StringComparison.OrdinalIgnoreCase) || value.Length == 0;

                if (expired) _cookies.Remove(name);
                else _cookies[name] = value;
            }

            return response;
        }
    }
}
