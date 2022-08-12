﻿using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EtheirysSynchronos.API;
using EtheirysSynchronosServer.Authentication;
using EtheirysSynchronosServer.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace EtheirysSynchronosServer.Hubs
{
    public partial class EthHub
    {
        private bool IsAdmin => _dbContext.Users.Single(b => b.UID == AuthenticatedUserId).IsAdmin;

        private bool IsModerator => _dbContext.Users.Single(b => b.UID == AuthenticatedUserId).IsModerator || IsAdmin;

        private List<string> OnlineAdmins => _dbContext.Users.Where(u => !string.IsNullOrEmpty(u.CharacterIdentification) && (u.IsModerator || u.IsAdmin))
                                .Select(u => u.UID).ToList();
        [Authorize(AuthenticationSchemes = SecretKeyAuthenticationHandler.AuthScheme)]
        [HubMethodName(Api.SendAdminChangeModeratorStatus)]
        public async Task ChangeModeratorStatus(string uid, bool isModerator)
        {
            if (!IsAdmin) return;
            var user = await _dbContext.Users.SingleOrDefaultAsync(u => u.UID == uid);

            if (user == null) return;

            user.IsModerator = isModerator;
            _dbContext.Update(user);
            await _dbContext.SaveChangesAsync();
            await Clients.Users(user.UID).SendAsync(Api.OnAdminForcedReconnect);
        }

        [Authorize(AuthenticationSchemes = SecretKeyAuthenticationHandler.AuthScheme)]
        [HubMethodName(Api.SendAdminDeleteBannedUser)]
        public async Task DeleteBannedUser(BannedUserDto dto)
        {
            if (!IsModerator || string.IsNullOrEmpty(dto.CharacterHash)) return;

            var existingUser =
                await _dbContext.BannedUsers.SingleOrDefaultAsync(b => b.CharacterIdentification == dto.CharacterHash);
            if (existingUser == null)
            {
                return;
            }

            _dbContext.Remove(existingUser);
            await _dbContext.SaveChangesAsync();
            await Clients.Users(OnlineAdmins).SendAsync(Api.OnAdminDeleteBannedUser, dto);
        }

        [Authorize(AuthenticationSchemes = SecretKeyAuthenticationHandler.AuthScheme)]
        [HubMethodName(Api.SendAdminDeleteForbiddenFile)]
        public async Task DeleteForbiddenFile(ForbiddenFileDto dto)
        {
            if (!IsAdmin || string.IsNullOrEmpty(dto.Hash)) return;

            var existingFile =
                await _dbContext.ForbiddenUploadEntries.SingleOrDefaultAsync(b => b.Hash == dto.Hash);
            if (existingFile == null)
            {
                return;
            }

            _dbContext.Remove(existingFile);
            await _dbContext.SaveChangesAsync();
            await Clients.Users(OnlineAdmins).SendAsync(Api.OnAdminDeleteForbiddenFile, dto);
        }

        [Authorize(AuthenticationSchemes = SecretKeyAuthenticationHandler.AuthScheme)]
        [HubMethodName(Api.InvokeAdminGetBannedUsers)]
        public async Task<List<BannedUserDto>> GetBannedUsers()
        {
            if (!IsModerator) return null;

            return await _dbContext.BannedUsers.AsNoTracking().Select(b => new BannedUserDto()
            {
                CharacterHash = b.CharacterIdentification,
                Reason = b.Reason
            }).ToListAsync();
        }

        [Authorize(AuthenticationSchemes = SecretKeyAuthenticationHandler.AuthScheme)]
        [HubMethodName(Api.InvokeAdminGetForbiddenFiles)]
        public async Task<List<ForbiddenFileDto>> GetForbiddenFiles()
        {
            if (!IsModerator) return null;

            return await _dbContext.ForbiddenUploadEntries.AsNoTracking().Select(b => new ForbiddenFileDto()
            {
                Hash = b.Hash,
                ForbiddenBy = b.ForbiddenBy
            }).ToListAsync();
        }

        [Authorize(AuthenticationSchemes = SecretKeyAuthenticationHandler.AuthScheme)]
        [HubMethodName(Api.InvokeAdminGetOnlineUsers)]
        public async Task<List<OnlineUserDto>> AdminGetOnlineUsers()
        {
            if (!IsModerator) return null;

            return await _dbContext.Users.AsNoTracking().Where(b => !string.IsNullOrEmpty(b.CharacterIdentification)).Select(b => new OnlineUserDto
            {
                CharacterNameHash = b.CharacterIdentification,
                UID = b.UID,
                IsModerator = b.IsModerator,
                IsAdmin = b.IsAdmin
            }).ToListAsync();
        }

        [Authorize(AuthenticationSchemes = SecretKeyAuthenticationHandler.AuthScheme)]
        [HubMethodName(Api.SendAdminUpdateOrAddBannedUser)]
        public async Task UpdateOrAddBannedUser(BannedUserDto dto)
        {
            if (!IsModerator || string.IsNullOrEmpty(dto.CharacterHash)) return;

            var existingUser =
                await _dbContext.BannedUsers.SingleOrDefaultAsync(b => b.CharacterIdentification == dto.CharacterHash);
            if (existingUser != null)
            {
                existingUser.Reason = dto.Reason;
                _dbContext.Update(existingUser);
            }
            else
            {
                await _dbContext.BannedUsers.AddAsync(new Banned
                {
                    CharacterIdentification = dto.CharacterHash,
                    Reason = dto.Reason
                });
            }

            await _dbContext.SaveChangesAsync();
            await Clients.Users(OnlineAdmins).SendAsync(Api.OnAdminUpdateOrAddBannedUser, dto);
            var bannedUser =
                await _dbContext.Users.SingleOrDefaultAsync(u => u.CharacterIdentification == dto.CharacterHash);
            if (bannedUser != null)
            {
                await Clients.User(bannedUser.UID).SendAsync(Api.OnAdminForcedReconnect);
            }
        }

        [Authorize(AuthenticationSchemes = SecretKeyAuthenticationHandler.AuthScheme)]
        [HubMethodName(Api.SendAdminUpdateOrAddForbiddenFile)]
        public async Task UpdateOrAddForbiddenFile(ForbiddenFileDto dto)
        {
            if (!IsAdmin || string.IsNullOrEmpty(dto.Hash)) return;

            var existingForbiddenFile =
                await _dbContext.ForbiddenUploadEntries.SingleOrDefaultAsync(b => b.Hash == dto.Hash);
            if (existingForbiddenFile != null)
            {
                existingForbiddenFile.ForbiddenBy = dto.ForbiddenBy;
                _dbContext.Update(existingForbiddenFile);
            }
            else
            {
                await _dbContext.ForbiddenUploadEntries.AddAsync(new ForbiddenUploadEntry
                {
                    Hash = dto.Hash,
                    ForbiddenBy = dto.ForbiddenBy
                });
            }

            await _dbContext.SaveChangesAsync();

            await Clients.Users(OnlineAdmins).SendAsync(Api.OnAdminUpdateOrAddForbiddenFile, dto);
        }
    }
}
