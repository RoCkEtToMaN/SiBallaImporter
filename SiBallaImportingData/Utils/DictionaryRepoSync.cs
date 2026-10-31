using System.Diagnostics;

namespace SiBallaImportingData.Utils
{
    // Copia del dizionario dei luoghi nel repo (data/DizionarioEventiInfo.json):
    // all'avvio la copia del repo sostituisce quella in bin, a fine giro torna nel repo con commit e push.
    // Su GitHub Actions commit e push li fa il workflow, che gira anche se il programma va in errore.
    // Gli errori di git vengono solo stampati: il programma va avanti comunque.
    public static class DictionaryRepoSync
    {
        private const string RepoRelativePath = "data/DizionarioEventiInfo.json";

        private static readonly bool OnGitHub = Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true";

        private static readonly string? RepoRoot = FindRepoRoot(AppContext.BaseDirectory);

        private static string RepoFile => Path.Combine(RepoRoot!, RepoRelativePath);

        public static void PullIntoBin(string binPath)
        {
            if (RepoRoot is null)
            {
                Console.WriteLine("Dizionario luoghi: repo non trovato, uso la copia in bin");
                return;
            }

            // ultima versione salvata dai run di GitHub; se il pull non riesce uso quella che c'è
            if (!OnGitHub && Git("pull", "--ff-only") != 0)
                Console.WriteLine("⚠️ Dizionario luoghi: git pull non riuscito, uso la copia locale del repo");

            if (!File.Exists(RepoFile))
            {
                Console.WriteLine($"Dizionario luoghi: {RepoRelativePath} non c'è nel repo, uso la copia in bin");
                return;
            }

            File.Copy(RepoFile, binPath, overwrite: true);
            Console.WriteLine($"Dizionario luoghi: preso dal repo ({RepoRelativePath})");
        }

        public static void PushFromBin(string binPath)
        {
            if (RepoRoot is null || !File.Exists(binPath)) return;

            Directory.CreateDirectory(Path.GetDirectoryName(RepoFile)!);
            File.Copy(binPath, RepoFile, overwrite: true);
            if (OnGitHub) return;

            Git("add", RepoRelativePath);
            if (Git("diff", "--cached", "--quiet", "--", RepoRelativePath) == 0)
            {
                Console.WriteLine("Dizionario luoghi: invariato, niente commit");
                return;
            }

            // solo il dizionario: le altre modifiche del working tree restano dove sono
            if (Git("commit", "-m", $"Dizionario luoghi: aggiornato da {Environment.MachineName}", "--", RepoRelativePath) != 0)
            {
                Console.WriteLine("⚠️ Dizionario luoghi: commit non riuscito");
                return;
            }

            if (Push() == 0)
            {
                Console.WriteLine("Dizionario luoghi: commit e push fatti");
                return;
            }

            // il remoto è avanti (es. un run di GitHub nel frattempo): riallineo e riprovo una volta
            if (Git("pull", "--rebase", "--autostash") != 0)
            {
                Git("rebase", "--abort");
                Console.WriteLine("⚠️ Dizionario luoghi: push non riuscito, il commit è in locale: fai pull e push a mano");
                return;
            }

            Console.WriteLine(Push() == 0
                ? "Dizionario luoghi: commit e push fatti"
                : "⚠️ Dizionario luoghi: push non riuscito, il commit è in locale: fai push a mano");
        }

        // su Windows il backend openssl di git non riesce a fare push in HTTPS
        private static int Push() => OperatingSystem.IsWindows()
            ? Git("-c", "http.sslBackend=schannel", "push")
            : Git("push");

        private static string? FindRepoRoot(string start)
        {
            for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
                if (Directory.Exists(Path.Combine(dir.FullName, ".git")) || File.Exists(Path.Combine(dir.FullName, ".git")))
                    return dir.FullName;
            return null;
        }

        private static int Git(params string[] args)
        {
            try
            {
                var psi = new ProcessStartInfo("git")
                {
                    WorkingDirectory = RepoRoot!,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                };
                foreach (var a in args) psi.ArgumentList.Add(a);

                using var p = Process.Start(psi)!;
                var stdout = p.StandardOutput.ReadToEndAsync();
                var stderr = p.StandardError.ReadToEndAsync();
                if (!p.WaitForExit(120_000))
                {
                    p.Kill(entireProcessTree: true);
                    Console.WriteLine($"   git {string.Join(' ', args)}: timeout");
                    return -1;
                }
                if (p.ExitCode != 0 && args[0] != "diff")
                    Console.WriteLine($"   git {string.Join(' ', args)}: {(stderr.Result + stdout.Result).Trim()}");
                return p.ExitCode;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   git {string.Join(' ', args)}: {ex.Message}");
                return -1;
            }
        }
    }
}
