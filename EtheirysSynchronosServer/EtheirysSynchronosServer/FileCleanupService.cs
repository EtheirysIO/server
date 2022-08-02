﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EtheirysSynchronosServer.Data;
using EtheirysSynchronosServer.Metrics;
using EtheirysSynchronosServer.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EtheirysSynchronosServer
{
    public class FileCleanupService : IHostedService, IDisposable
    {
        private readonly ILogger<FileCleanupService> _logger;
        private readonly IServiceProvider _services;
        private readonly IConfiguration _configuration;
        private Timer _timer;

        public FileCleanupService(ILogger<FileCleanupService> logger, IServiceProvider services, IConfiguration configuration)
        {
            _logger = logger;
            _services = services;
            _configuration = configuration;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("File Cleanup Service started");

            _timer = new Timer(CleanUpFiles, null, TimeSpan.Zero, TimeSpan.FromMinutes(10));

            return Task.CompletedTask;
        }

        private void CleanUpFiles(object state)
        {
            if (!int.TryParse(_configuration["UnusedFileRetentionPeriodInDays"], out var filesOlderThanDays))
            {
                filesOlderThanDays = 7;
            }

            _logger.LogInformation($"Cleaning up files older than {filesOlderThanDays} days");

            try
            {
                using var scope = _services.CreateScope();
                var dbContext = scope.ServiceProvider.GetService<MareDbContext>()!;

                var prevTime = DateTime.Now.Subtract(TimeSpan.FromDays(filesOlderThanDays));

                var allFiles = dbContext.Files.Where(f => f.Uploaded).ToList();
                foreach (var file in allFiles)
                {
                    var fileName = Path.Combine(_configuration["CacheDirectory"], file.Hash);
                    var fi = new FileInfo(fileName);
                    if (!fi.Exists)
                    {
                        _logger.LogInformation("File does not exist anymore: " + fileName);
                        dbContext.Files.Remove(file);
                    }
                    else if (fi.LastAccessTime < prevTime)
                    {
                        MareMetrics.FilesTotalSize.Dec(fi.Length);
                        _logger.LogInformation("File outdated: " + fileName);
                        dbContext.Files.Remove(file);
                        File.Delete(fileName);
                    }
                }

                if (!bool.TryParse(_configuration["PurgeUnusedAccounts"], out var purgeUnusedAccounts))
                {
                    purgeUnusedAccounts = false;
                }

                if (purgeUnusedAccounts)
                {
                    if (!int.TryParse(_configuration["PurgeUnusedAccountsPeriodInDays"], out var usersOlderThanDays))
                    {
                        usersOlderThanDays = 14;
                    }

                    _logger.LogInformation($"Cleaning up users older than {usersOlderThanDays} days");

                    var allUsers = dbContext.Users.ToList();
                    List<User> usersToRemove = new();
                    foreach (var user in allUsers)
                    {
                        if (user.LastLoggedIn < (DateTime.Now - TimeSpan.FromDays(usersOlderThanDays)))
                        {
                            _logger.LogInformation("User outdated: " + user.UID);
                            usersToRemove.Add(user);
                        }
                    }

                    foreach (var user in usersToRemove)
                    {
                        var auth = dbContext.Auth.Single(a => a.UserUID == user.UID);

                        var userFiles = dbContext.Files.Where(f => f.Uploaded && f.Uploader.UID == user.UID).ToList();
                        foreach (var file in userFiles)
                        {
                            var fi = new FileInfo(Path.Combine(_configuration["CacheDirectory"], file.Hash));
                            if (fi.Exists)
                            {
                                MareMetrics.FilesTotalSize.Dec(fi.Length);
                                MareMetrics.FilesTotal.Dec();
                                fi.Delete();
                            }
                        }

                        dbContext.Files.RemoveRange(userFiles);

                        var ownPairData = dbContext.ClientPairs.Where(u => u.User.UID == user.UID).ToList();

                        dbContext.RemoveRange(ownPairData);
                        var otherPairData = dbContext.ClientPairs.Include(u => u.User)
                            .Where(u => u.OtherUser.UID == user.UID).ToList();

                        MareMetrics.Pairs.Dec(ownPairData.Count);
                        MareMetrics.PairsPaused.Dec(ownPairData.Count(c => c.IsPaused));
                        MareMetrics.Pairs.Dec(otherPairData.Count);
                        MareMetrics.PairsPaused.Dec(otherPairData.Count(c => c.IsPaused));
                        MareMetrics.UsersRegistered.Dec();

                        dbContext.RemoveRange(otherPairData);
                        dbContext.Remove(auth);
                        dbContext.Remove(user);
                    }
                }

                _logger.LogInformation($"Cleanup complete");

                dbContext.SaveChanges();
            }
            catch
            {
            }
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _timer?.Change(Timeout.Infinite, 0);

            return Task.CompletedTask;
        }

        public void Dispose()
        {
            _timer?.Dispose();
        }
    }
}
