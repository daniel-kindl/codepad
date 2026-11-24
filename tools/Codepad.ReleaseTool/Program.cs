using System.Globalization;
using Codepad.Application.Updates;
using Codepad.Infrastructure.Updates;

namespace Codepad.ReleaseTool;

/// <summary>
/// <c>codepad-release</c>: the commands of the owner for a release. It builds packages, makes and signs the manifest and checks the result with the same
/// code that the app uses. It never uploads, tags or publishes anything. The private key is read only from the file that the owner names.
/// </summary>
internal static class Program
{
    private const string USAGE = """
        codepad-release - build, sign and check a CodePad release. It does not publish anything.

          keygen   --out DIR                      Make a signing key pair. Keep release-private.pem secret.
          package  --platform P --version V --out DIR [--repo DIR] [--timestamp ISO]
                                                  Build the full-install archive for linux-x64 or win-x64. Run "dotnet restore" first.
          manifest --version V --notes FILE --linux FILE --windows FILE --out FILE [--published ISO]
                                                  Write manifest.json from the archives.
          sign     --key FILE --manifest FILE     Write manifest.json.sig next to the manifest.
          verify   --public-key FILE --manifest FILE [--archive FILE]...
                                                  Check the signature, the manifest rules, and the archives against the manifest.
          checksums --out FILE FILE...            Write a SHA256SUMS file.
        """;

    public static int Main(string[] args)
    {
        try
        {
            if (args.Length == 0 || args[0] is "--help" or "-h" or "help")
            {
                Console.Out.WriteLine(USAGE);
                return args.Length == 0 ? 2 : 0;
            }

            Dictionary<string, List<string>> options = ParseOptions(args.Skip(1).ToArray(), out List<string> positional);
            return args[0] switch
            {
                "keygen" => KeyGen(Required(options, "--out")),
                "package" => Package(options),
                "manifest" => Manifest(options),
                "sign" => Sign(Required(options, "--key"), Required(options, "--manifest")),
                "verify" => Verify(Required(options, "--public-key"), Required(options, "--manifest"), options.GetValueOrDefault("--archive") ?? []),
                "checksums" => Checksums(Required(options, "--out"), positional),
                _ => Fail($"Unknown command '{args[0]}'."),
            };
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or IOException or UpdateManifestException or UnauthorizedAccessException)
        {
            return Fail(exception.Message);
        }
    }

    internal static int KeyGen(string folder)
    {
        Directory.CreateDirectory(folder);
        string privatePath = Path.Combine(folder, "release-private.pem");
        string publicPath = Path.Combine(folder, "release-public.pem");
        if (File.Exists(privatePath) || File.Exists(publicPath))
        {
            return Fail("A key file exists already. The tool does not replace a key.");
        }

        (string privatePem, string publicPem) = ReleaseSigning.NewKeyPair();

        // The private key file is readable by the owner only, from the first byte.
        FileStreamOptions secret = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
        if (!OperatingSystem.IsWindows())
        {
            secret.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        using (FileStream stream = new FileStream(privatePath, secret))
        using (StreamWriter writer = new StreamWriter(stream))
        {
            writer.Write(privatePem);
        }

        File.WriteAllText(publicPath, publicPem);
        Console.Out.WriteLine("Private key (secret, keep it outside the repository): " + privatePath);
        Console.Out.WriteLine("Public key (put it in src/Codepad.Desktop/Updates/UpdatePublicKey.pem): " + publicPath);
        return 0;
    }

    internal static int Package(Dictionary<string, List<string>> options)
    {
        string platform = Required(options, "--platform");
        if (platform is not ("linux-x64" or "win-x64"))
        {
            return Fail("The platform must be linux-x64 or win-x64.");
        }

        string version = Required(options, "--version");
        if (!SemanticVersion.TryParse(version, out SemanticVersion? parsed) || !parsed!.IsStable)
        {
            return Fail("The version must be a stable SemVer version, for example 0.2.0.");
        }

        string repo = options.GetValueOrDefault("--repo")?.FirstOrDefault() ?? Directory.GetCurrentDirectory();
        DateTimeOffset timestamp = options.GetValueOrDefault("--timestamp")?.FirstOrDefault() is string text
            ? DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal)
            : new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        string archive = Packager.Package(repo, platform, parsed.ToString(), Required(options, "--out"), timestamp);
        Console.Out.WriteLine(archive);
        return 0;
    }

    internal static int Manifest(Dictionary<string, List<string>> options)
    {
        string version = Required(options, "--version");
        if (!SemanticVersion.TryParse(version, out SemanticVersion? parsed) || !parsed!.IsStable)
        {
            return Fail("The version must be a stable SemVer version, for example 0.2.0.");
        }

        string notes = File.ReadAllText(Required(options, "--notes"));
        DateTimeOffset published = options.GetValueOrDefault("--published")?.FirstOrDefault() is string text
            ? DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal)
            : new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        byte[] manifest = ReleaseManifestBuilder.Build(parsed.ToString(), notes, published, [new ReleaseArchive("linux-x64", Required(options, "--linux")), new ReleaseArchive("win-x64", Required(options, "--windows"))]);

        // The same rules as the app: a manifest that the app would reject must not leave the tool.
        _ = UpdateManifestParser.Parse(manifest);
        File.WriteAllBytes(Required(options, "--out"), manifest);
        return 0;
    }

    internal static int Sign(string keyFile, string manifestFile)
    {
        byte[] signature = ReleaseSigning.Sign(File.ReadAllText(keyFile), File.ReadAllBytes(manifestFile));
        File.WriteAllBytes(manifestFile + ".sig", signature);
        return 0;
    }

    internal static int Verify(string publicKeyFile, string manifestFile, IReadOnlyList<string> archives)
    {
        byte[] manifestBytes = File.ReadAllBytes(manifestFile);
        EcdsaManifestVerifier verifier = new EcdsaManifestVerifier(File.ReadAllText(publicKeyFile));
        if (!verifier.HasKey)
        {
            return Fail("The public key file is not an ECDSA P-256 public key.");
        }

        if (!verifier.Verify(manifestBytes, File.ReadAllBytes(manifestFile + ".sig")))
        {
            return Fail("The signature does not match the manifest and the public key.");
        }

        UpdateManifest manifest = UpdateManifestParser.Parse(manifestBytes);
        foreach (string archive in archives)
        {
            UpdateAsset? asset = manifest.Assets.FirstOrDefault(item => item.Name == Path.GetFileName(archive));
            if (asset is null)
            {
                return Fail($"The manifest has no asset named {Path.GetFileName(archive)}.");
            }

            byte[] bytes = File.ReadAllBytes(archive);
            if (bytes.Length != asset.Size || Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes)) != asset.Sha256)
            {
                return Fail($"The archive {Path.GetFileName(archive)} does not match the size or the digest in the manifest.");
            }
        }

        Console.Out.WriteLine($"OK: version {manifest.Version}, {manifest.Assets.Count} assets, signature valid.");
        return 0;
    }

    internal static int Checksums(string outFile, IReadOnlyList<string> files)
    {
        if (files.Count == 0)
        {
            return Fail("Give the files to list.");
        }

        File.WriteAllText(outFile, ReleaseManifestBuilder.Checksums(files));
        return 0;
    }

    private static Dictionary<string, List<string>> ParseOptions(string[] tokens, out List<string> positional)
    {
        Dictionary<string, List<string>> options = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        positional = [];
        for (int index = 0; index < tokens.Length; index++)
        {
            if (tokens[index].StartsWith("--", StringComparison.Ordinal))
            {
                if (index + 1 >= tokens.Length)
                {
                    throw new ArgumentException($"The option {tokens[index]} needs a value.");
                }

                if (!options.TryGetValue(tokens[index], out List<string>? values))
                {
                    values = [];
                    options[tokens[index]] = values;
                }

                values.Add(tokens[++index]);
            }
            else
            {
                positional.Add(tokens[index]);
            }
        }

        return options;
    }

    private static string Required(Dictionary<string, List<string>> options, string name) =>
        options.TryGetValue(name, out List<string>? values) && values.Count > 0 ? values[0] : throw new ArgumentException($"Give {name}.");

    private static int Fail(string message)
    {
        Console.Error.WriteLine("codepad-release: " + message);
        return 1;
    }
}
