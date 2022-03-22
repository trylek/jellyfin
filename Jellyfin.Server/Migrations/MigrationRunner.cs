using System;
using System.Linq;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Server.Migrations
{
    /// <summary>
    /// The class that knows which migrations to apply and how to apply them.
    /// </summary>
    public sealed class MigrationRunner
    {
        /// <summary>
        /// The list of known migrations, in order of applicability.
        /// </summary>
        private static readonly Type[] _migrationTypes =
        {
            typeof(Routines.DisableTranscodingThrottling),
            typeof(Routines.CreateUserLoggingConfigFile),
            typeof(Routines.MigrateActivityLogDb),
            typeof(Routines.RemoveDuplicateExtras),
            typeof(Routines.AddDefaultPluginRepository),
            // typeof(Routines.MigrateUserDb),
            typeof(Routines.ReaddDefaultPluginRepository),
            typeof(Routines.MigrateDisplayPreferencesDb),
            typeof(Routines.RemoveDownloadImagesInAdvance),
            typeof(Routines.AddPeopleQueryIndex)
        };

        /// <summary>
        /// Run all needed migrations.
        /// </summary>
        /// <param name="host">CoreAppHost that hosts current version.</param>
        /// <param name="loggerFactory">Factory for making the logger.</param>
        public static void Run(CoreAppHost host, ILoggerFactory loggerFactory)
        {
            Console.WriteLine("\n\nIn MigrationRunner.Run()...");
            Console.WriteLine("Creating variables...");
            var logger = loggerFactory.CreateLogger<MigrationRunner>();
            Console.WriteLine("Created logger...");
            Console.WriteLine("#######");
            Console.WriteLine(host.ServiceProvider);
            Console.WriteLine(host.ServiceProvider.GetType());
            Console.WriteLine("#######");
            var migrations = _migrationTypes
                .Select(m => ActivatorUtilities.CreateInstance(host.ServiceProvider, m))
                .OfType<IMigrationRoutine>()
                .ToArray();
            Console.WriteLine("Created Migrations...");
            var migrationOptions = ((IConfigurationManager)host.ConfigurationManager).GetConfiguration<MigrationOptions>(MigrationsListStore.StoreKey);
            Console.WriteLine("Created Migration Options...");

            Console.WriteLine("Checking !host.ConfigurationManager.Configuration.IsStartupWizardCompleted && migrationOptions.Applied.Count == 0...");
            if (!host.ConfigurationManager.Configuration.IsStartupWizardCompleted && migrationOptions.Applied.Count == 0)
            {
                Console.WriteLine("Condition fulfilled...");
                // If startup wizard is not finished, this is a fresh install.
                // Don't run any migrations, just mark all of them as applied.
                logger.LogInformation("Marking all known migrations as applied because this is a fresh install");
                migrationOptions.Applied.AddRange(migrations.Where(m => !m.PerformOnNewInstall).Select(m => (m.Id, m.Name)));
                host.ConfigurationManager.SaveConfiguration(MigrationsListStore.StoreKey, migrationOptions);
            }

            Console.WriteLine("Creating AppliedMigrationId's...");
            var appliedMigrationIds = migrationOptions.Applied.Select(m => m.Id).ToHashSet();

            Console.WriteLine("About to start Migrations loop...");
            for (var i = 0; i < migrations.Length; i++)
            {
                Console.WriteLine("\nIteration {0}", i);
                var migrationRoutine = migrations[i];
                if (appliedMigrationIds.Contains(migrationRoutine.Id))
                {
                    Console.WriteLine("Skipping migration '{0}' since it is already applied", migrationRoutine.Name);
                    logger.LogDebug("Skipping migration '{Name}' since it is already applied", migrationRoutine.Name);
                    continue;
                }

                Console.WriteLine("Applying migration '{0}'", migrationRoutine.Name);
                logger.LogInformation("Applying migration '{Name}'", migrationRoutine.Name);

                try
                {
                    Console.WriteLine("Trying to perform the migration routine...");
                    migrationRoutine.Perform();
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Could not apply migration '{0}'", migrationRoutine.Name);
                    logger.LogError(ex, "Could not apply migration '{Name}'", migrationRoutine.Name);
                    throw;
                }

                // Mark the migration as completed
                logger.LogInformation("Migration '{Name}' applied successfully", migrationRoutine.Name);
                migrationOptions.Applied.Add((migrationRoutine.Id, migrationRoutine.Name));
                host.ConfigurationManager.SaveConfiguration(MigrationsListStore.StoreKey, migrationOptions);
                logger.LogDebug("Migration '{Name}' marked as applied in configuration.", migrationRoutine.Name);
            }
        }
    }
}
