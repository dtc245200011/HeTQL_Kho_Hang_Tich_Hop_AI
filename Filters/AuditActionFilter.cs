using DuAnCode.Web.Data;
using DuAnCode.Web.Models;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;

namespace DuAnCode.Web.Filters
{
    // Comprehensive audit filter: records method/path/action, masks sensitive data, skips static assets
    public class AuditActionFilter : IAsyncActionFilter
    {
        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var http = context.HttpContext;

            // Noise filtering: ignore static assets and known non-action paths
            var path = http.Request.Path.ToString() ?? string.Empty;
            var lowerPath = path.ToLowerInvariant();
            if (Regex.IsMatch(lowerPath, @"\.(css|js|png|jpg|jpeg|ico|woff2?)$") || lowerPath.Contains("/swagger") || lowerPath.Contains("/_vs/"))
            {
                await next();
                return;
            }

            var method = http.Request.Method.ToUpperInvariant();
            var actionDisplay = context.ActionDescriptor?.DisplayName ?? string.Empty;

            string? beforeJson = null;
            try
            {
                if (method == "GET")
                {
                    beforeJson = http.Request.QueryString.HasValue ? http.Request.QueryString.Value : string.Empty;
                }
                else
                {
                    http.Request.EnableBuffering();
                    if (http.Request.HasFormContentType)
                    {
                        var form = await http.Request.ReadFormAsync();
                        beforeJson = MaskFormCollection(form);
                    }
                    else
                    {
                        using var sr = new StreamReader(http.Request.Body, leaveOpen: true);
                        var body = await sr.ReadToEndAsync();
                        http.Request.Body.Position = 0;
                        beforeJson = MaskJsonString(body);
                    }
                }
            }
            catch { beforeJson = null; }

            var resultContext = await next();

            var performedBy = http.User?.Identity?.Name;
            if (string.IsNullOrWhiteSpace(performedBy))
            {
                if (!string.IsNullOrWhiteSpace(actionDisplay) && actionDisplay.ToLowerInvariant().Contains("login"))
                {
                    try
                    {
                        if (http.Request.HasFormContentType)
                        {
                            var form = await http.Request.ReadFormAsync();
                            var uname = form["username"].FirstOrDefault() ?? form["userName"].FirstOrDefault() ?? form["UserName"].FirstOrDefault() ?? form["email"].FirstOrDefault();
                            if (!string.IsNullOrWhiteSpace(uname)) performedBy = uname + " (login attempt)";
                        }
                        else
                        {
                            http.Request.EnableBuffering();
                            using var sr2 = new StreamReader(http.Request.Body, leaveOpen: true);
                            var body2 = await sr2.ReadToEndAsync();
                            http.Request.Body.Position = 0;
                            var extracted = ExtractUsernameFromJson(body2);
                            if (!string.IsNullOrWhiteSpace(extracted)) performedBy = extracted + " (login attempt)";
                        }
                    }
                    catch { }
                }
            }
            performedBy ??= "anonymous";

            string? afterJson = null;
            try
            {
                if (method != "GET")
                {
                    var args = new Dictionary<string, object?>();
                    foreach (var kv in context.ActionArguments)
                    {
                        args[kv.Key] = kv.Value;
                    }
                    afterJson = JsonSerializer.Serialize(MaskObject(args), new JsonSerializerOptions { WriteIndented = true });
                }
            }
            catch { afterJson = null; }

            var actionString = $"[{method}] {path} - {actionDisplay}";

            try
            {
                var db = http.RequestServices.GetRequiredService<ApplicationDbContext>();
                var log = new AuditLog
                {
                    EntityType = actionDisplay ?? "action",
                    EntityId = Guid.NewGuid().ToString(),
                    Action = actionString,
                    BeforeJson = string.IsNullOrWhiteSpace(beforeJson) ? null : beforeJson,
                    AfterJson = string.IsNullOrWhiteSpace(afterJson) ? null : afterJson,
                    PerformedBy = performedBy,
                    PerformedAt = DateTime.UtcNow,
                    Reason = resultContext.Exception?.Message
                };
                await db.AuditLogs.AddAsync(log);
                await db.SaveChangesAsync();
            }
            catch { }
        }

        // Helpers
        private static string MaskFormCollection(IFormCollection form)
        {
            var dict = new Dictionary<string, object?>();
            foreach (var k in form.Keys)
            {
                var v = form[k].ToString();
                dict[k] = IsSensitiveKey(k) ? "[PROTECTED]" : v;
            }
            return JsonSerializer.Serialize(dict, new JsonSerializerOptions { WriteIndented = true });
        }

        private static string? MaskJsonString(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try
            {
                var node = JsonNode.Parse(json);
                if (node == null) return json;
                MaskJsonNode(node);
                return node.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
            }
            catch
            {
                return MaskKeyValuePairs(json);
            }
        }

        private static void MaskJsonNode(JsonNode node)
        {
            if (node is JsonObject obj)
            {
                var keys = obj.Select(p => p.Key).ToList();
                foreach (var key in keys)
                {
                    var child = obj[key];
                    if (IsSensitiveKey(key))
                    {
                        obj[key] = JsonValue.Create("[PROTECTED]");
                    }
                    else if (child is JsonObject || child is JsonArray)
                    {
                        MaskJsonNode(child!);
                    }
                }
            }
            else if (node is JsonArray arr)
            {
                for (int i = 0; i < arr.Count; i++)
                {
                    var child = arr[i];
                    if (child is JsonObject || child is JsonArray) MaskJsonNode(child!);
                }
            }
        }

        private static object MaskObject(object? obj)
        {
            if (obj == null) return obj ?? new { };
            try
            {
                var json = JsonSerializer.Serialize(obj);
                var masked = MaskJsonString(json);
                if (masked == null) return obj;
                return JsonSerializer.Deserialize<object>(masked) ?? masked;
            }
            catch
            {
                return obj;
            }
        }

        private static bool IsSensitiveKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return false;
            var k = key.ToLowerInvariant();
            return k.Contains("password") || k.Contains("confirm") || k.Contains("secret") || k.Contains("token") || k.Contains("pwd") || k.Contains("credential") || k.Contains("ssn") || k.Contains("card");
        }

        private static string ExtractUsernameFromJson(string body)
        {
            try
            {
                var node = JsonNode.Parse(body);
                if (node is JsonObject obj)
                {
                    foreach (var candidate in new[] { "username", "userName", "email", "user" })
                    {
                        if (obj.TryGetPropertyValue(candidate, out var val) && val != null)
                        {
                            return val.ToString() ?? string.Empty;
                        }
                    }
                }
            }
            catch { }
            return string.Empty;
        }

        private static string MaskKeyValuePairs(string s)
        {
            // naive: replace password=... tokens (case-insensitive)
            return Regex.Replace(s, @"(?i)(password|confirmPassword|oldPassword|newPassword|secret|token)=[^&\s]+", "$1=[PROTECTED]");
        }
    }
}
