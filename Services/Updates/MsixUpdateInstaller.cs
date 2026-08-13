using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Windows.Management.Deployment;
using Networker.Core.Updates;
using Windows.Foundation;

namespace networker.Services.Updates
{
    /// <summary>
    /// Stages a verified MSIX through the Windows App SDK
    /// <see cref="PackageDeploymentManager"/>. No force flags, no unsigned
    /// packages, no downgrade: Windows enforces same-publisher, higher-version
    /// rules and defers registration while Networker is running. Completion means
    /// <em>staged</em> — Networker keeps running and the user restarts.
    /// </summary>
    public sealed class MsixUpdateInstaller : IUpdateInstaller
    {
        private readonly IUpdateLog _log;

        public MsixUpdateInstaller(IUpdateLog log)
        {
            _log = log;
        }

        public async Task<UpdateInstallResult> InstallAsync(VerifiedPackage package, CancellationToken cancellationToken)
        {
            try
            {
                var options = new AddPackageOptions
                {
                    AllowUnsigned = false,
                    DeferRegistrationWhenPackagesAreInUse = true,
                    ForceAppShutdown = false,
                    ForceTargetAppShutdown = false,
                    ForceUpdateFromAnyVersion = false,
                    RetainFilesOnFailure = false,
                };

                if (options.IsExpectedDigestsSupported)
                {
                    options.ExpectedDigests.Add(new KeyValuePair<Uri, string>(
                        new Uri(package.PackagePath),
                        package.ExpectedDigest));
                }

                IAsyncOperationWithProgress<PackageDeploymentResult, PackageDeploymentProgress> operation =
                    PackageDeploymentManager.GetDefault().AddPackageByUriAsync(
                        new Uri(package.PackagePath),
                        options);

                operation.Progress = (_, progress) =>
                {
                    _log.Debug($"Deployment {progress.Status}: {progress.Progress:P0}.");
                };

                // Deployment runs to completion even if the caller cancels: a
                // cancelled deployment can leave pending state. No token is passed.
                PackageDeploymentResult result = await operation.AsTask().ConfigureAwait(false);

                if (result.Status != PackageDeploymentStatus.CompletedSuccess)
                {
                    _log.Error(
                        $"Deployment failed: status={result.Status} errorText={result.ErrorText} "
                        + $"error={result.Error} activity={result.ActivityId}");
                    return new UpdateInstallResult(false, result.ErrorText, result.Error?.Message, result.ActivityId);
                }

                _log.Info($"Deployment staged successfully; activity={result.ActivityId}.");
                return new UpdateInstallResult(true, null, null, result.ActivityId);
            }
            catch (Exception ex)
            {
                _log.Error($"Deployment threw: {ex.GetType().Name}.", ex);
                return new UpdateInstallResult(false, ex.Message, ex.GetType().Name, null);
            }
        }
    }
}
