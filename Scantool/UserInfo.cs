using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Scantool;

public class UserInfo
{
    public string? Id { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Gender { get; set; }
    public string? Email { get; set; }
    public string? BirthDate { get; set; }
    public string? CategoryID { get; set; }
    public string? CategoryTitle { get; set; }
    public string? CategorySlug { get; set; }
    public string? RedirectUri { get; set; }

    private static string NormalizeScannerGender(string? gender)
    {
        var normalized = (gender ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "male" or "mann" or "m" => "0",
            "female" or "frau" or "f" => "1",
            _ => "0"
        };
    }

    public static UserInfo? FromJson(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            return new UserInfo
            {
                Id = root.GetProperty("id").GetString(),
                FirstName = root.GetProperty("firstName").GetString(),
                LastName = root.GetProperty("lastName").GetString(),
                Gender = NormalizeScannerGender(root.GetProperty("gender").GetString()),
                Email = root.GetProperty("email").GetString(),
                BirthDate = root.GetProperty("birthDate").GetString(),
                CategoryID = root.GetProperty("categoryId").GetInt16().ToString(),
                CategoryTitle = root.GetProperty("categoryTitle").GetString(),
                CategorySlug = root.GetProperty("categorySlug").GetString(),
                RedirectUri = root.GetProperty("redirectUri").GetString(),
            };
        }
        catch (Exception ex)
        {
            throw new Exception($"[EXCEPTION] UserInfo.FromJson: {ex.Message}");
        }
    }
}
