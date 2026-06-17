using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace OfficeTaskManagement.Services.Codebase
{
    public class GitCloneService
    {
        private readonly ILogger<GitCloneService> _logger;

        public GitCloneService(ILogger<GitCloneService> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Clones a remote repository to a target local path using git clone --depth 1.
        /// </summary>
        public async Task<string> CloneRepositoryAsync(string repositoryUrl, string targetDirectory, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(repositoryUrl))
                throw new ArgumentException("Repository URL cannot be null or empty.", nameof(repositoryUrl));

            if (Directory.Exists(targetDirectory))
            {
                var gitDir = Path.Combine(targetDirectory, ".git");
                if (Directory.Exists(gitDir))
                {
                    _logger.LogInformation("Target directory {Path} exists and contains a .git repository. Running git pull...", targetDirectory);
                    var pullStartInfo = new ProcessStartInfo
                    {
                        FileName = "git",
                        Arguments = "pull",
                        WorkingDirectory = targetDirectory,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    using var pullProcess = new Process { StartInfo = pullStartInfo };
                    try
                    {
                        pullProcess.Start();
                        var pullOutputTask = pullProcess.StandardOutput.ReadToEndAsync(ct);
                        var pullErrorTask = pullProcess.StandardError.ReadToEndAsync(ct);

                        await pullProcess.WaitForExitAsync(ct);

                        if (pullProcess.ExitCode == 0)
                        {
                            _logger.LogInformation("Git pull completed successfully for {Path}.", targetDirectory);
                            return targetDirectory;
                        }
                        else
                        {
                            var pullErrorMsg = await pullErrorTask;
                            _logger.LogWarning("Git pull failed with exit code {ExitCode}. Error: {Error}. Progressing with existing directory.", pullProcess.ExitCode, pullErrorMsg);
                            return targetDirectory;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to run Git pull in {Path}. Progressing with existing directory.", targetDirectory);
                        return targetDirectory;
                    }
                }
                else
                {
                    try
                    {
                        _logger.LogInformation("Target directory {Path} exists but is not a Git repository. Attempting to delete and clean it...", targetDirectory);
                        Directory.Delete(targetDirectory, true);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to delete existing non-git directory {Path}. Progressing with existing files without cloning.", targetDirectory);
                        return targetDirectory;
                    }
                }
            }

            Directory.CreateDirectory(targetDirectory);

            _logger.LogInformation("Cloning remote repository {Url} to {Path}...", repositoryUrl, targetDirectory);

            var startInfo = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = $"clone --depth 1 \"{repositoryUrl}\" \"{targetDirectory}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = startInfo };
            
            try
            {
                process.Start();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start Git process. Please verify Git is installed and in the system PATH.");
                throw new InvalidOperationException("Git executable not found on the server. Please install Git.", ex);
            }

            var outputTask = process.StandardOutput.ReadToEndAsync(ct);
            var errorTask = process.StandardError.ReadToEndAsync(ct);

            await process.WaitForExitAsync(ct);

            if (process.ExitCode != 0)
            {
                var errorMsg = await errorTask;
                _logger.LogError("Git clone failed with exit code {ExitCode}. Error output: {Error}", process.ExitCode, errorMsg);
                throw new InvalidOperationException($"Git clone failed with exit code {process.ExitCode}. Error: {errorMsg}");
            }

            _logger.LogInformation("Git clone completed successfully for {Url}.", repositoryUrl);
            return targetDirectory;
        }
    }
}
