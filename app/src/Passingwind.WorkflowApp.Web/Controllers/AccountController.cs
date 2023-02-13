﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Volo.Abp;
using Volo.Abp.Account;
using Volo.Abp.Account.Settings;
using Volo.Abp.Account.Web;
using Volo.Abp.AspNetCore.Mvc;
using Volo.Abp.Identity;
using Volo.Abp.Identity.AspNetCore;
using Volo.Abp.Security.Claims;
using Volo.Abp.Settings;
using IdentityUser = Volo.Abp.Identity.IdentityUser;

namespace Passingwind.WorkflowApp.Web.Controllers;

[RemoteService(Name = AccountRemoteServiceConsts.RemoteServiceName)]
[Controller]
[ControllerName("Login")]
[Area("account")]
[Route("api/account")]
public class AccountController : AbpController
{
    private readonly ISettingProvider _settingsProvider;
    private readonly AbpSignInManager _signInManager;
    private readonly IAuthenticationSchemeProvider _authenticationSchemeProvider;
    private readonly IdentitySecurityLogManager _identitySecurityLogManager;
    private readonly IdentityUserManager _userManager;
    private readonly IdentityRoleManager _roleManager;
    private readonly AbpAccountOptions _accountOptions;
    private readonly IOptions<IdentityOptions> _identityOptions;

    public AccountController(ISettingProvider settingsProvider, AbpSignInManager signInManager, IAuthenticationSchemeProvider authenticationSchemeProvider, IdentitySecurityLogManager identitySecurityLogManager, IdentityUserManager userManager, IdentityRoleManager roleManager, IOptions<IdentityOptions> identityOptions, IOptions<AbpAccountOptions> accountOptions)
    {
        _settingsProvider = settingsProvider;
        _signInManager = signInManager;
        _authenticationSchemeProvider = authenticationSchemeProvider;
        _identitySecurityLogManager = identitySecurityLogManager;
        _userManager = userManager;
        _roleManager = roleManager;
        _identityOptions = identityOptions;
        _accountOptions = accountOptions.Value;
    }

    [HttpGet("login")]
    [AllowAnonymous]
    public async Task<AccountResultDto> GetAsync()
    {
        AccountResultDto dto = new AccountResultDto
        {
            ExternalProviders = await GetExternalProviders(),

            EnableLocalLogin = await _settingsProvider.IsTrueAsync(AccountSettingNames.EnableLocalLogin)
        };

        return dto;
    }

    [HttpGet("login/external")]
    public IActionResult ExternalLoginAsync(string provider)
    {
        string redirectUrl = new Uri($"{Request.Scheme}://{Request.Host.ToUriComponent()}/auth/login/external/callback").ToString();

        AuthenticationProperties properties = _signInManager.ConfigureExternalAuthenticationProperties(provider, redirectUrl);

        properties.Items["scheme"] = provider;

        return Challenge(properties, provider);
    }

    [HttpGet("login/external/callback")]
    public async Task<AbpLoginResult> ExternalLoginCallbackAsync(string remoteError = null)
    {
        if (remoteError != null)
        {
            Logger.LogWarning($"External login callback error: {remoteError}");
            throw new UserFriendlyException($"External login callback error: {remoteError}");
        }

        ExternalLoginInfo loginInfo = await _signInManager.GetExternalLoginInfoAsync();
        if (loginInfo == null)
        {
            Logger.LogWarning("External login info is not available");
            throw new UserFriendlyException("External login info is not available");
        }

        Microsoft.AspNetCore.Identity.SignInResult signInResult = await _signInManager.ExternalLoginSignInAsync(
            loginInfo.LoginProvider,
            loginInfo.ProviderKey,
            isPersistent: false,
            bypassTwoFactor: false
        );

        Logger.LogInformation($"External login result: {signInResult}");

        await _identitySecurityLogManager.SaveAsync(new IdentitySecurityLogContext()
        {
            Identity = IdentitySecurityLogIdentityConsts.IdentityExternal,
            Action = "Login" + signInResult
        });

        string email = loginInfo.Principal.FindFirstValue(AbpClaimTypes.Email);

        if (email.IsNullOrWhiteSpace())
        {
            Logger.LogWarning($"External login user email is null.");
            throw new UserFriendlyException("Cannot proceed because user email is null!");
        }

        IdentityUser user = await _userManager.FindByEmailAsync(email);
        if (user == null)
        {
            user = await CreateExternalUserAsync(loginInfo);
        }
        else
        {
            if (await _userManager.FindByLoginAsync(loginInfo.LoginProvider, loginInfo.ProviderKey) == null)
            {
                CheckIdentityErrors(await _userManager.AddLoginAsync(user, loginInfo));
            }
        }

        if (signInResult.RequiresTwoFactor)
        {
            return LoginHelper.GetAbpLoginResult(signInResult);
        }
        else
        {
            await _signInManager.SignInAsync(user, false);
        }

        await _identityOptions.SetAsync();

        await _identitySecurityLogManager.SaveAsync(new IdentitySecurityLogContext()
        {
            Identity = IdentitySecurityLogIdentityConsts.IdentityExternal,
            Action = signInResult.ToIdentitySecurityLogAction(),
            UserName = user.Name,
        });

        return LoginHelper.GetAbpLoginResult(signInResult);
    }

    protected virtual async Task<List<ExternalProviderDto>> GetExternalProviders()
    {
        IEnumerable<AuthenticationScheme> schemes = await _authenticationSchemeProvider.GetAllSchemesAsync();

        return schemes
             .Where(x => x.DisplayName != null || x.Name.Equals(_accountOptions.WindowsAuthenticationSchemeName, StringComparison.OrdinalIgnoreCase))
             .Select(x => new ExternalProviderDto
             {
                 DisplayName = x.DisplayName,
                 AuthenticationScheme = x.Name
             })
             .ToList();
    }

    protected virtual async Task<IdentityUser> CreateExternalUserAsync(ExternalLoginInfo info)
    {
        await _identityOptions.SetAsync();

        string emailAddress = info.Principal.FindFirstValue(AbpClaimTypes.Email);
        string emailVerified = info.Principal.FindFirstValue(AbpClaimTypes.EmailVerified);

        IdentityUser user = new IdentityUser(GuidGenerator.Create(), emailAddress, emailAddress, CurrentTenant.Id);

        CheckIdentityErrors(await _userManager.CreateAsync(user));
        CheckIdentityErrors(await _userManager.SetEmailAsync(user, emailAddress));
        CheckIdentityErrors(await _userManager.AddLoginAsync(user, info));
        CheckIdentityErrors(await _userManager.AddDefaultRolesAsync(user));

        user.Name = info.Principal.FindFirstValue(AbpClaimTypes.Name);
        user.Surname = info.Principal.FindFirstValue(AbpClaimTypes.SurName);
        user.SetEmailConfirmed(emailVerified == "true");

        string phoneNumber = info.Principal.FindFirstValue(AbpClaimTypes.PhoneNumber);
        if (!phoneNumber.IsNullOrWhiteSpace())
        {
            bool phoneNumberConfirmed = string.Equals(info.Principal.FindFirstValue(AbpClaimTypes.PhoneNumberVerified), "true", StringComparison.InvariantCultureIgnoreCase);
            user.SetPhoneNumber(phoneNumber, phoneNumberConfirmed);
        }

        await _userManager.UpdateAsync(user);

        return user;
    }

    protected virtual void CheckIdentityErrors(IdentityResult identityResult)
    {
        if (!identityResult.Succeeded)
        {
            throw new UserFriendlyException("Operation failed: " + identityResult.Errors.Select(e => $"[{e.Code}] {e.Description}").JoinAsString(", "));
        }
    }
}