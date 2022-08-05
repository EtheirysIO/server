﻿using System;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Threading.Tasks;
using EtheirysSynchronos.API;
using EtheirysSynchronosServer.Data;
using EtheirysSynchronosServer.Metrics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace EtheirysSynchronosServer.Hubs
{
    public partial class MareHub : Hub
    {
        private readonly SystemInfoService _systemInfoService;
        private readonly IConfiguration _configuration;
        private readonly IHttpContextAccessor _contextAccessor;
        private readonly ILogger<MareHub> _logger;
        private readonly MareDbContext _dbContext;

        public MareHub(MareDbContext mareDbContext, ILogger<MareHub> logger, SystemInfoService systemInfoService, IConfiguration configuration, IHttpContextAccessor contextAccessor)
        {
            _systemInfoService = systemInfoService;
            _configuration = configuration;
            _contextAccessor = contextAccessor;
            _logger = logger;
            _dbContext = mareDbContext;
        }

        [HubMethodName(Api.InvokeHeartbeat)]
        public async Task<ConnectionDto> Heartbeat(string characterIdentification)
        {
            MareMetrics.InitializedConnections.Inc();

            var userId = Context.User!.Claims.SingleOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value;

            _logger.LogInformation("Connection from " + userId + ", CI: " + characterIdentification);

            await Clients.Caller.SendAsync(Api.OnUpdateSystemInfo, _systemInfoService.SystemInfoDto);

            var isBanned = await _dbContext.BannedUsers.AsNoTracking().AnyAsync(u => u.CharacterIdentification == characterIdentification);

            if (!string.IsNullOrEmpty(userId) && !isBanned && !string.IsNullOrEmpty(characterIdentification))
            {
                _logger.LogInformation("Connection from " + userId);
                var user = (await _dbContext.Users.SingleAsync(u => u.UID == userId));
                if (!string.IsNullOrEmpty(user.CharacterIdentification) && characterIdentification != user.CharacterIdentification)
                {
                    return new ConnectionDto()
                    {
                        ServerVersion = Api.Version
                    };
                }
                else if (string.IsNullOrEmpty(user.CharacterIdentification))
                {
                    MareMetrics.AuthorizedConnections.Inc();
                }

                user.LastLoggedIn = DateTime.UtcNow;
                user.CharacterIdentification = characterIdentification;
                await _dbContext.SaveChangesAsync();
                return new ConnectionDto
                {
                    ServerVersion = Api.Version,
                    UID = userId,
                    IsModerator = user.IsModerator,
                    IsAdmin = user.IsAdmin
                };
            }

            return new ConnectionDto()
            {
                ServerVersion = Api.Version
            };
        }

        [HubMethodName(Api.InvokeGetSystemInfo)]
        public async Task<SystemInfoDto> GetSystemInfo()
        {
            return _systemInfoService.SystemInfoDto;
        }

        public override Task OnConnectedAsync()
        {
            var feature = Context.Features.Get<IHttpContextAccessor>();
            _logger.LogInformation("Connection from " + _contextAccessor.GetIpAddress());
            MareMetrics.Connections.Inc();
            return base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception exception)
        {
            MareMetrics.Connections.Dec();

            var user = await _dbContext.Users.AsNoTracking().SingleOrDefaultAsync(u => u.UID == AuthenticatedUserId);
            if (user != null && !string.IsNullOrEmpty(user.CharacterIdentification))
            {
                MareMetrics.AuthorizedConnections.Dec();
                _logger.LogInformation("Disconnect from " + AuthenticatedUserId);

                var otherUsers = await _dbContext.ClientPairs.AsNoTracking()
                    .Include(u => u.User)
                    .Include(u => u.OtherUser)
                    .Where(w => w.User.UID == user.UID && !w.IsPaused)
                    .Where(w => !string.IsNullOrEmpty(w.OtherUser.CharacterIdentification))
                    .Select(e => e.OtherUser).ToListAsync();
                var otherEntries = await _dbContext.ClientPairs.AsNoTracking().Include(u => u.User)
                    .Where(u => otherUsers.Any(e => e == u.User) && u.OtherUser.UID == user.UID && !u.IsPaused).ToListAsync();
                await Clients.Users(otherEntries.Select(e => e.User.UID)).SendAsync(Api.OnUserRemoveOnlinePairedPlayer, user.CharacterIdentification);

                var notUploadedFiles = _dbContext.Files.Where(f => !f.Uploaded && f.Uploader.UID == user.UID).ToList();
                _dbContext.RemoveRange(notUploadedFiles);

                (await _dbContext.Users.SingleAsync(u => u.UID == AuthenticatedUserId)).CharacterIdentification = null;
                await _dbContext.SaveChangesAsync();
            }

            await base.OnDisconnectedAsync(exception);
        }

        public static string GenerateRandomString(int length, string allowableChars = null)
        {
            if (string.IsNullOrEmpty(allowableChars))
                allowableChars = @"ABCDEFGHJKLMNPQRSTUVWXYZ0123456789";

            // Generate random data
            var rnd = new byte[length];
            using (var rng = new RNGCryptoServiceProvider())
                rng.GetBytes(rnd);

            // Generate the output string
            var allowable = allowableChars.ToCharArray();
            var l = allowable.Length;
            var chars = new char[length];
            for (var i = 0; i < length; i++)
                chars[i] = allowable[rnd[i] % l];

            return new string(chars);
        }

        protected string AuthenticatedUserId => Context.User?.Claims?.SingleOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "Unknown";

        protected async Task<Models.User> GetAuthenticatedUserUntrackedAsync()
        {
            return await _dbContext.Users.AsNoTrackingWithIdentityResolution().SingleAsync(u => u.UID == AuthenticatedUserId);
        }
    }
}
