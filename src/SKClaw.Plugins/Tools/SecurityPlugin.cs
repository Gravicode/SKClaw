using System.ComponentModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.SemanticKernel;

namespace SKClaw.Plugins.Tools;

/// <summary>
/// SecurityPlugin — Password generation, strength checking, encryption/decryption,
/// JWT decoding, certificate info, and secure data utilities.
/// </summary>
public class SecurityPlugin
{
    // ── Password ───────────────────────────────────────────────

    [KernelFunction, Description("Generate a strong random password with configurable complexity")]
    public string GeneratePassword(
        [Description("Password length (8-128)")] int length = 20,
        [Description("Include uppercase letters?")] bool uppercase = true,
        [Description("Include lowercase letters?")] bool lowercase = true,
        [Description("Include numbers?")] bool numbers = true,
        [Description("Include symbols (!@#$%^&*)?")] bool symbols = true,
        [Description("Exclude ambiguous characters (0O1lI)?")] bool excludeAmbiguous = false)
    {
        length = Math.Clamp(length, 8, 128);
        var pool = new StringBuilder();
        if (uppercase) pool.Append(excludeAmbiguous ? "ABCDEFGHJKLMNPQRSTUVWXYZ" : "ABCDEFGHIJKLMNOPQRSTUVWXYZ");
        if (lowercase) pool.Append(excludeAmbiguous ? "abcdefghjkmnpqrstuvwxyz" : "abcdefghijklmnopqrstuvwxyz");
        if (numbers)   pool.Append(excludeAmbiguous ? "23456789" : "0123456789");
        if (symbols)   pool.Append("!@#$%^&*()-_=+[]{}|;:,.<>?");

        if (pool.Length == 0) return "Error: At least one character type must be selected.";

        var chars = pool.ToString();
        var bytes = new byte[length];
        using var rng = RandomNumberGenerator.Create();

        // Rejection sampling for uniform distribution
        var result = new char[length];
        for (int i = 0; i < length; )
        {
            rng.GetBytes(bytes);
            foreach (var b in bytes)
            {
                if (i >= length) break;
                int idx = b % chars.Length;
                if (b < (256 / chars.Length) * chars.Length) // avoid modulo bias
                    result[i++] = chars[idx];
            }
        }

        var password = new string(result);
        var strength = CheckPasswordStrengthInternal(password);
        return $"Password: {password}\nStrength : {strength}";
    }

    [KernelFunction, Description("Generate a memorable passphrase using random words")]
    public string GeneratePassphrase(
        [Description("Number of words (3-8)")] int wordCount = 4,
        [Description("Separator character")] string separator = "-",
        [Description("Capitalize each word?")] bool capitalize = true,
        [Description("Add a number at the end?")] bool addNumber = true)
    {
        wordCount = Math.Clamp(wordCount, 3, 8);
        string[] wordList =
        {
            "apple","brave","cloud","dance","eagle","flame","grace","house","ivory","jewel",
            "knife","lemon","magic","noble","ocean","piano","queen","river","stone","tiger",
            "ultra","vivid","witch","xenon","yacht","zebra","amber","blast","crisp","drift",
            "ember","frost","globe","haste","inlet","joust","karma","lunar","merge","night",
            "orbit","pixel","query","radar","storm","trend","unity","vault","waves","xenial",
            "yield","zonal","acute","blend","chord","depot","elite","flair","grant","helix",
            "image","joint","knack","laser","mercy","nexus","optic","prism","quest","realm",
            "sigma","torch","umbra","vigor","wrath","xylem","yodel","zephyr"
        };

        using var rng = RandomNumberGenerator.Create();
        var words = new List<string>();
        var bytes = new byte[wordCount * 4];
        rng.GetBytes(bytes);
        for (int i = 0; i < wordCount; i++)
        {
            int idx = (int)(BitConverter.ToUInt32(bytes, i * 4) % (uint)wordList.Length);
            var word = wordList[idx];
            words.Add(capitalize ? char.ToUpper(word[0]) + word[1..] : word);
        }

        if (addNumber)
        {
            var numBytes = new byte[4];
            rng.GetBytes(numBytes);
            words.Add((BitConverter.ToUInt32(numBytes) % 9000 + 1000).ToString());
        }

        var passphrase = string.Join(separator, words);
        var entropy    = Math.Log2(Math.Pow(wordList.Length, wordCount)) + (addNumber ? Math.Log2(9000) : 0);
        return $"Passphrase: {passphrase}\nEntropy   : ~{entropy:F0} bits";
    }

    [KernelFunction, Description("Analyse the strength of a password and provide improvement suggestions")]
    public string CheckPasswordStrength([Description("Password to analyse")] string password)
    {
        var score  = 0;
        var issues = new List<string>();
        var tips   = new List<string>();

        if (password.Length >= 8)  { score++; } else issues.Add("Too short (< 8 chars)");
        if (password.Length >= 12) { score++; } else tips.Add("Use 12+ characters");
        if (password.Length >= 16) { score++; } else tips.Add("Use 16+ characters for maximum security");
        if (Regex.IsMatch(password, @"[A-Z]")) { score++; } else tips.Add("Add uppercase letters");
        if (Regex.IsMatch(password, @"[a-z]")) { score++; } else tips.Add("Add lowercase letters");
        if (Regex.IsMatch(password, @"\d"))    { score++; } else tips.Add("Add numbers");
        if (Regex.IsMatch(password, @"[!@#$%^&*()_+\-=\[\]{};':""\\|,.<>\/?]")) { score++; } else tips.Add("Add symbols");
        if (!Regex.IsMatch(password, @"(.)\1{2,}")) { score++; } else issues.Add("Avoid repeated chars (aaa, 111)");
        if (!Regex.IsMatch(password, @"(012|123|234|345|456|567|678|789|890|abc|bcd|cde)")) { score++; } else issues.Add("Avoid sequential patterns");

        // Entropy estimate
        int poolSize = 0;
        if (Regex.IsMatch(password, @"[a-z]")) poolSize += 26;
        if (Regex.IsMatch(password, @"[A-Z]")) poolSize += 26;
        if (Regex.IsMatch(password, @"\d"))    poolSize += 10;
        if (Regex.IsMatch(password, @"[^a-zA-Z\d]")) poolSize += 32;
        double entropy = password.Length * Math.Log2(poolSize > 0 ? poolSize : 1);

        string rating = score switch
        {
            >= 8 => "💪 Very Strong",
            >= 6 => "✅ Strong",
            >= 4 => "⚠️ Moderate",
            >= 2 => "❌ Weak",
            _    => "🚨 Very Weak"
        };

        var sb = new StringBuilder($"Password Analysis\n{new string('-', 40)}\n");
        sb.AppendLine($"Rating   : {rating} ({score}/9)");
        sb.AppendLine($"Length   : {password.Length} chars");
        sb.AppendLine($"Entropy  : ~{entropy:F0} bits");
        if (issues.Count > 0) sb.AppendLine($"Issues   : {string.Join(", ", issues)}");
        if (tips.Count > 0)   sb.AppendLine($"Tips     : {string.Join(" | ", tips.Take(3))}");
        return sb.ToString().TrimEnd();
    }

    // ── Encryption / Decryption ────────────────────────────────

    [KernelFunction, Description("Encrypt text using AES-256-GCM with a passphrase")]
    public string EncryptText(
        [Description("Plain text to encrypt")] string plainText,
        [Description("Passphrase (keep this secret!)")] string passphrase)
    {
        try
        {
            // Derive key from passphrase using PBKDF2
            var salt     = RandomNumberGenerator.GetBytes(32);
            using var kdf = new Rfc2898DeriveBytes(passphrase, salt, 200_000, HashAlgorithmName.SHA256);
            var key      = kdf.GetBytes(32);
            var nonce    = RandomNumberGenerator.GetBytes(12); // 96-bit nonce for GCM
            var tag      = new byte[16];
            var plainBytes = Encoding.UTF8.GetBytes(plainText);
            var cipherBytes = new byte[plainBytes.Length];

            using var aes = new AesGcm(key, 16);
            aes.Encrypt(nonce, plainBytes, cipherBytes, tag);

            // Pack: salt(32) + nonce(12) + tag(16) + ciphertext
            var packed = new byte[32 + 12 + 16 + cipherBytes.Length];
            Buffer.BlockCopy(salt,        0, packed, 0,  32);
            Buffer.BlockCopy(nonce,       0, packed, 32, 12);
            Buffer.BlockCopy(tag,         0, packed, 44, 16);
            Buffer.BlockCopy(cipherBytes, 0, packed, 60, cipherBytes.Length);

            return $"SKCLAW_ENC:{Convert.ToBase64String(packed)}";
        }
        catch (Exception ex) { return $"Encryption error: {ex.Message}"; }
    }

    [KernelFunction, Description("Decrypt text that was encrypted with EncryptText")]
    public string DecryptText(
        [Description("Encrypted text (starts with SKCLAW_ENC:)")] string encryptedText,
        [Description("Passphrase used during encryption")] string passphrase)
    {
        try
        {
            if (!encryptedText.StartsWith("SKCLAW_ENC:"))
                return "Error: Not a SKClaw-encrypted string.";

            var packed = Convert.FromBase64String(encryptedText["SKCLAW_ENC:".Length..]);
            var salt   = packed[..32];
            var nonce  = packed[32..44];
            var tag    = packed[44..60];
            var cipher = packed[60..];

            using var kdf = new Rfc2898DeriveBytes(passphrase, salt, 200_000, HashAlgorithmName.SHA256);
            var key = kdf.GetBytes(32);

            var plain = new byte[cipher.Length];
            using var aes = new AesGcm(key, 16);
            aes.Decrypt(nonce, cipher, tag, plain);

            return Encoding.UTF8.GetString(plain);
        }
        catch (CryptographicException) { return "❌ Decryption failed: Wrong passphrase or corrupted data."; }
        catch (Exception ex)           { return $"Error: {ex.Message}"; }
    }

    // ── Hashing ────────────────────────────────────────────────

    [KernelFunction, Description("Compute cryptographic hashes of text: MD5, SHA1, SHA256, SHA384, SHA512, BLAKE2")]
    public string ComputeHash(
        [Description("Text to hash")] string text,
        [Description("Algorithm: md5, sha1, sha256, sha384, sha512")] string algorithm = "sha256",
        [Description("Output format: hex or base64")] string format = "hex")
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        byte[] hash = algorithm.ToLower() switch
        {
            "md5"    => MD5.HashData(bytes),
            "sha1"   => SHA1.HashData(bytes),
            "sha256" => SHA256.HashData(bytes),
            "sha384" => SHA384.HashData(bytes),
            "sha512" => SHA512.HashData(bytes),
            _        => SHA256.HashData(bytes)
        };
        var result = format.ToLower() == "base64"
            ? Convert.ToBase64String(hash)
            : Convert.ToHexString(hash).ToLower();
        return $"{algorithm.ToUpper()}: {result}";
    }

    [KernelFunction, Description("Hash a password using BCrypt-equivalent (PBKDF2 with high iterations)")]
    public string HashPassword(
        [Description("Password to hash")] string password,
        [Description("Iterations (higher = slower = more secure)")] int iterations = 200000)
    {
        var salt = RandomNumberGenerator.GetBytes(32);
        using var kdf = new Rfc2898DeriveBytes(password, salt, iterations, HashAlgorithmName.SHA256);
        var hash = kdf.GetBytes(32);
        var packed = $"pbkdf2-sha256${iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
        return $"Hashed: {packed}";
    }

    [KernelFunction, Description("Verify a password against a PBKDF2 hash created by HashPassword")]
    public string VerifyPassword(
        [Description("Plain text password")] string password,
        [Description("Hash string created by HashPassword")] string hashString)
    {
        try
        {
            var parts = hashString.Replace("Hashed: ", "").Split('$');
            if (parts.Length < 4 || parts[0] != "pbkdf2-sha256") return "❌ Invalid hash format.";
            int iterations   = int.Parse(parts[1]);
            var salt = Convert.FromBase64String(parts[2]);
            var expectedHash = Convert.FromBase64String(parts[3]);

            using var kdf = new Rfc2898DeriveBytes(password, salt, iterations, HashAlgorithmName.SHA256);
            var actualHash = kdf.GetBytes(32);
            bool match = CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
            return match ? "✅ Password matches!" : "❌ Password does not match.";
        }
        catch (Exception ex) { return $"Error: {ex.Message}"; }
    }

    // ── JWT ────────────────────────────────────────────────────

    [KernelFunction, Description("Decode and inspect a JWT token (without verifying the signature)")]
    public string DecodeJwt([Description("JWT token string")] string jwt)
    {
        try
        {
            var parts = jwt.Trim().Split('.');
            if (parts.Length != 3) return "Not a valid JWT (expected header.payload.signature).";

            string DecodeBase64(string s)
            {
                s = s.Replace('-', '+').Replace('_', '/');
                switch (s.Length % 4) { case 2: s += "=="; break; case 3: s += "="; break; }
                return Encoding.UTF8.GetString(Convert.FromBase64String(s));
            }

            var header  = JsonDocument.Parse(DecodeBase64(parts[0]));
            var payload = JsonDocument.Parse(DecodeBase64(parts[1]));

            var sb = new StringBuilder("🔑 JWT Token Analysis\n\n");
            sb.AppendLine("=== HEADER ===");
            sb.AppendLine(JsonSerializer.Serialize(header.RootElement, new JsonSerializerOptions { WriteIndented = true }));
            sb.AppendLine("\n=== PAYLOAD ===");
            sb.AppendLine(JsonSerializer.Serialize(payload.RootElement, new JsonSerializerOptions { WriteIndented = true }));

            // Check expiry
            if (payload.RootElement.TryGetProperty("exp", out var exp))
            {
                var expTime = DateTimeOffset.FromUnixTimeSeconds(exp.GetInt64());
                bool isExpired = expTime < DateTimeOffset.UtcNow;
                sb.AppendLine($"\n=== EXPIRY ===");
                sb.AppendLine($"Expires : {expTime:yyyy-MM-dd HH:mm:ss} UTC");
                sb.AppendLine($"Status  : {(isExpired ? "⛔ EXPIRED" : $"✅ Valid for {(expTime - DateTimeOffset.UtcNow).TotalHours:F1} hours")}");
            }

            sb.AppendLine("\n⚠️ Signature NOT verified (no secret key provided).");
            return sb.ToString().TrimEnd();
        }
        catch (Exception ex) { return $"JWT decode error: {ex.Message}"; }
    }

    // ── Security Utilities ─────────────────────────────────────

    [KernelFunction, Description("Generate a cryptographically secure random token (API key, session token, etc.)")]
    public string GenerateToken(
        [Description("Token format: hex, base64, base64url, uuid")] string format = "hex",
        [Description("Byte length (entropy): 16, 32, 64")] int bytes = 32)
    {
        bytes = bytes switch { 16 => 16, 64 => 64, _ => 32 };
        var raw = RandomNumberGenerator.GetBytes(bytes);
        return format.ToLower() switch
        {
            "base64"    => Convert.ToBase64String(raw),
            "base64url" => Convert.ToBase64String(raw).Replace('+', '-').Replace('/', '_').TrimEnd('='),
            "uuid"      => Guid.NewGuid().ToString(),
            _           => Convert.ToHexString(raw).ToLower()
        };
    }

    [KernelFunction, Description("Compute HMAC-SHA256 signature for webhook validation or API requests")]
    public string ComputeHmac(
        [Description("Message to sign")] string message,
        [Description("Secret key")] string secret,
        [Description("Algorithm: sha256, sha512")] string algorithm = "sha256",
        [Description("Output: hex or base64")] string format = "hex")
    {
        var keyBytes = Encoding.UTF8.GetBytes(secret);
        var msgBytes = Encoding.UTF8.GetBytes(message);
        byte[] hash  = algorithm.ToLower() == "sha512"
            ? new HMACSHA512(keyBytes).ComputeHash(msgBytes)
            : new HMACSHA256(keyBytes).ComputeHash(msgBytes);
        var result = format.ToLower() == "base64" ? Convert.ToBase64String(hash) : Convert.ToHexString(hash).ToLower();
        return $"HMAC-{algorithm.ToUpper()}: {result}";
    }

    [KernelFunction, Description("Scan a text or URL for common security issues: exposed secrets, open redirects, SQL patterns")]
    public string ScanForSecurityIssues([Description("Text or URL to scan")] string input)
    {
        var issues = new List<string>();

        // Detect common secret patterns
        var patterns = new Dictionary<string, string>
        {
            [@"sk-[a-zA-Z0-9]{40,}"]                                 = "OpenAI API Key",
            [@"AIza[0-9A-Za-z\-_]{35}"]                              = "Google API Key",
            [@"[Aa]uth[orizationKey\s:=]+['""]?[A-Za-z0-9+/=]{20,}"] = "Authorization credential",
            [@"password\s*[=:]\s*['""][^'""]{4,}['""]"]             = "Hardcoded password",
            [@"private.key|BEGIN RSA|BEGIN EC"]                       = "Private key material",
            [@"mongodb\+srv://[^:]+:[^@]+@"]                          = "MongoDB connection string with credentials",
            [@"jdbc:[a-z]+://[^:]+:[^@]+@"]                           = "JDBC connection string with credentials",
            [@"https?://[^/]+/.*[?&](redirect|url|next|return)=http"] = "Open redirect pattern",
            [@"(?:union|select|insert|drop|delete|update)\s+(?:all\s+)?(?:from|into|table)"] = "SQL injection pattern",
            [@"<script[^>]*>.*?</script>"]                             = "Inline script (XSS risk)",
        };

        foreach (var (pattern, label) in patterns)
            if (Regex.IsMatch(input, pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline))
                issues.Add($"⚠️ {label}");

        // Check for weak entropy in random-looking strings
        if (Regex.IsMatch(input, @"\b(123456|password|admin|letmein|qwerty)\b", RegexOptions.IgnoreCase))
            issues.Add("⚠️ Weak/common password detected");

        if (issues.Count == 0) return "✅ No obvious security issues detected.";
        return $"🔍 Security Scan Results:\n" + string.Join("\n", issues);
    }

    [KernelFunction, Description("Encode sensitive data to prevent accidental exposure in logs")]
    public string MaskSensitiveData(
        [Description("Text that may contain sensitive data")] string text,
        [Description("Masking mode: partial (show first/last 4), full, or redact")] string mode = "partial")
    {
        // Mask credit card numbers
        text = Regex.Replace(text, @"\b(\d{4})\d{8}(\d{4})\b",
            mode == "full" ? "****-****-****-****" : "$1-****-****-$2");

        // Mask email addresses
        text = Regex.Replace(text, @"([a-zA-Z0-9._%+\-]{2})[a-zA-Z0-9._%+\-]+(@[a-zA-Z0-9.\-]+)",
            mode == "redact" ? "[REDACTED EMAIL]" : "$1***$2");

        // Mask API keys / tokens (long alphanumeric strings)
        text = Regex.Replace(text, @"\b([A-Za-z0-9]{4})[A-Za-z0-9]{12,}([A-Za-z0-9]{4})\b",
            mode == "redact" ? "[REDACTED]" : "$1...$2");

        return text;
    }

    // ── Private ────────────────────────────────────────────────
    private static string CheckPasswordStrengthInternal(string password)
    {
        int score = 0;
        if (password.Length >= 12) score++;
        if (password.Length >= 16) score++;
        if (Regex.IsMatch(password, @"[A-Z]")) score++;
        if (Regex.IsMatch(password, @"[a-z]")) score++;
        if (Regex.IsMatch(password, @"\d"))    score++;
        if (Regex.IsMatch(password, @"[^a-zA-Z\d]")) score++;
        return score >= 5 ? "Very Strong 💪" : score >= 4 ? "Strong ✅" : score >= 3 ? "Moderate ⚠️" : "Weak ❌";
    }
}
