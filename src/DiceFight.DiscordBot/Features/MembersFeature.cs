using Discord;
using Discord.Net;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DiceFight.DiscordBot.Features;

// "members": people say who they are (MemberDirectory).
//
//   /introduce        a form for their real name and email. The name becomes
//                     their server nickname; the email gets a 6-digit code,
//                     and typing it back gives them the verified role (which
//                     the server's private channels can then require).
//   /verify code      the code, typed as a command - the same as the form's button
//   /whois member     (managers) the name and email someone gave
//
//   "Members": {
//     "VerifiedRoleId": 1234567890,
//     "ManagerRoleIds": [ 1234567890 ],
//     "Smtp": { "Host": "smtp.gmail.com", "UserName": "...", "From": "...", "FromName": "Barnabas" }
//   }
//
// The bot's role needs Manage Nicknames and Manage Roles, and must sit above
// the members' roles and the verified role in the server's role list.
// Discord never lets a bot rename the server owner.
public sealed class MembersFeature(MemberDirectoryStore directories, VerificationCodes codes, IEmailSender email) : IBotFeature
{
    public const string FeatureId = "members";
    private const string Introduce = "introduce";
    private const string Verify = "verify";
    private const string WhoIs = "whois";

    public sealed class Settings
    {
        // The role a verified email earns. None: verifying just records it.
        public ulong? VerifiedRoleId { get; set; }
        public List<ulong> ManagerRoleIds { get; set; } = [];
        // Set the real name as the nickname (on by default).
        public bool SetNickname { get; set; } = true;
        // The name in the email ("Your code for <ServerName>"); default: the server's name.
        public string? ServerName { get; set; }
    }

    public string Id => FeatureId;

    private static Settings Read(BotContext bot) => bot.Settings.GetSection("Members").Get<Settings>() ?? new Settings();
    private static SmtpSettings Smtp(BotContext bot) => SmtpSettings.Read(bot.Settings.GetSection("Members:Smtp"));

    public IReadOnlyList<SlashCommandProperties> BuildCommands(BotContext bot) =>
    [
        new SlashCommandBuilder()
            .WithName(Introduce)
            .WithDescription("Tell us your real name (it becomes your nickname here) and verify your email")
            .WithContextTypes(InteractionContextType.Guild)
            .Build(),
        new SlashCommandBuilder()
            .WithName(Verify)
            .WithDescription("Enter the code we emailed you")
            .WithContextTypes(InteractionContextType.Guild)
            .AddOption("code", ApplicationCommandOptionType.String, "The 6-digit code from the email", isRequired: true)
            .Build(),
        new SlashCommandBuilder()
            .WithName(WhoIs)
            .WithDescription("The real name and email a member gave (managers only)")
            .WithContextTypes(InteractionContextType.Guild)
            // Hidden from everyone without Manage Nicknames until a server admin says otherwise.
            .WithDefaultMemberPermissions(GuildPermission.ManageNicknames)
            .AddOption("member", ApplicationCommandOptionType.User, "Who", isRequired: true)
            .Build(),
    ];

    public async Task HandleAsync(SocketSlashCommand command, BotContext bot)
    {
        if (command.User is not SocketGuildUser user)
        {
            await command.RespondAsync("Use this in the server.", ephemeral: true);
            return;
        }
        switch (command.Data.Name)
        {
            case Introduce:
                await command.RespondWithModalAsync(await FormAsync(user));
                break;
            case Verify:
                await command.DeferAsync(ephemeral: true);
                await command.FollowupAsync(await CheckCodeAsync(user, command.Data.Options.First().Value as string, bot), ephemeral: true);
                break;
            case WhoIs:
                await WhoIsAsync(command, user, bot);
                break;
        }
    }

    public async Task ButtonAsync(SocketMessageComponent button, BotContext bot)
    {
        if (button.User is not SocketGuildUser user) return;
        switch (button.Data.CustomId)
        {
            case $"{Introduce}:open":
                await button.RespondWithModalAsync(await FormAsync(user));
                break;
            case $"{Introduce}:code":
                await button.RespondWithModalAsync(new ModalBuilder()
                    .WithTitle("Enter your code")
                    .WithCustomId($"{Introduce}:verify")
                    .AddTextInput("The 6-digit code from the email", "code", TextInputStyle.Short, "123456", minLength: 6, maxLength: 12, required: true)
                    .Build());
                break;
            case $"{Introduce}:resend":
                await button.DeferAsync(ephemeral: true);
                var address = codes.PendingEmail(user.Guild.Id, user.Id)
                    ?? await directories.UseAsync(DirectoryId(user), d => (d.Find(user.Id)?.Email, false));
                if (address is null)
                    await button.FollowupAsync("There's no email to send a code to - use `/introduce` first.", ephemeral: true);
                else
                    await SendCodeAsync(button, user, address, bot);
                break;
        }
    }

    public async Task ModalAsync(SocketModal modal, BotContext bot)
    {
        if (modal.User is not SocketGuildUser user) return;
        string? Field(string id) => modal.Data.Components.FirstOrDefault(c => c.CustomId == id)?.Value;
        switch (modal.Data.CustomId)
        {
            case $"{Introduce}:form":
                await SubmitFormAsync(modal, user, Field("name"), Field("email"), bot);
                break;
            case $"{Introduce}:verify":
                await modal.DeferAsync(ephemeral: true);
                await modal.FollowupAsync(await CheckCodeAsync(user, Field("code"), bot), ephemeral: true);
                break;
        }
    }

    private static string DirectoryId(SocketGuildUser user) => user.Guild.Id.ToString();

    // The form, filled in with what they gave last time.
    private async Task<Modal> FormAsync(SocketGuildUser user)
    {
        var known = await directories.UseAsync(DirectoryId(user), d => (d.Find(user.Id), false));
        return new ModalBuilder()
            .WithTitle("Introduce yourself")
            .WithCustomId($"{Introduce}:form")
            .AddTextInput("Your first and last name", "name", TextInputStyle.Short, "e.g. Jane Smith",
                minLength: 2, maxLength: MemberDirectory.MaxNameLength, required: true, value: known?.RealName)
            .AddTextInput("Your email (we'll send a code to check it)", "email", TextInputStyle.Short, "you@example.com",
                maxLength: 254, required: false, value: known?.Email)
            .Build();
    }

    private async Task SubmitFormAsync(SocketModal modal, SocketGuildUser user, string? typedName, string? typedEmail, BotContext bot)
    {
        var (name, nameProblem) = MemberDirectory.CleanName(typedName);
        var (address, emailProblem) = MemberDirectory.CleanEmail(typedEmail);
        if ((nameProblem ?? emailProblem) is { } problem)
        {
            await modal.RespondAsync(problem, ephemeral: true, components: new ComponentBuilder()
                .WithButton("Try again", $"{Introduce}:open", ButtonStyle.Primary).Build());
            return;
        }
        await modal.DeferAsync(ephemeral: true);
        var settings = Read(bot);
        var saved = await directories.UseAsync(DirectoryId(user), d =>
            (d.Introduce(user.Id, user.Username, name!, address, DateTime.UtcNow), true));
        var said = new List<string> { $"Thanks, {name}!" };

        if (settings.SetNickname && user.DisplayName != name)
        {
            if (user.Guild.OwnerId == user.Id)
                said.Add("Discord doesn't let bots change the server owner's nickname - you'll need to set yours yourself.");
            else
                try
                {
                    await user.ModifyAsync(p => p.Nickname = name);
                    said.Add($"Your nickname here is now **{name}**.");
                }
                catch (HttpException ex)
                {
                    bot.Logger.LogWarning(ex, "Bot {Bot}: couldn't set {User}'s nickname", bot.Name, user.Id);
                    said.Add("I couldn't change your nickname - an admin needs to give my role *Manage Nicknames* and move it above yours.");
                }
        }

        if (address is null)
        {
            await modal.FollowupAsync(string.Join("\n", said), ephemeral: true);
            return;
        }
        if (saved.VerifiedEmail == address)
        {
            said.Add($"{address} is already verified.");
            if (await GrantRoleAsync(user, settings, bot) is { } roleNote) said.Add(roleNote);
            await modal.FollowupAsync(string.Join("\n", said), ephemeral: true);
            return;
        }
        await modal.FollowupAsync(string.Join("\n", said), ephemeral: true);
        await SendCodeAsync(modal, user, address, bot);
    }

    private async Task SendCodeAsync(SocketInteraction interaction, SocketGuildUser user, string address, BotContext bot)
    {
        var smtp = Smtp(bot);
        if (!smtp.Configured)
        {
            await interaction.FollowupAsync("Email checking isn't switched on yet, so your email is saved but not verified. We'll let you know when it is.", ephemeral: true);
            return;
        }
        var (code, wait) = codes.Issue(user.Guild.Id, user.Id, address);
        if (code is null)
        {
            await interaction.FollowupAsync($"A code was just sent - check {address} (and its spam folder). You can ask for another in {Math.Ceiling(wait.TotalSeconds)} seconds.",
                ephemeral: true, components: CodeButtons());
            return;
        }
        var server = Read(bot).ServerName is { Length: > 0 } n ? n : user.Guild.Name;
        try
        {
            await email.SendAsync(smtp, address, $"Your code for {server}: {code}",
                $"Hi {user.DisplayName},\n\nYour code to verify this email address on the {server} Discord is:\n\n    {code}\n\n" +
                $"Enter it with the \"Enter code\" button, or type /verify {code} in the server. It expires in {VerificationCodes.Lifetime.TotalMinutes:0} minutes.\n\n" +
                "If you didn't ask for this, you can ignore this email.\n");
        }
        catch (Exception ex)
        {
            bot.Logger.LogError(ex, "Bot {Bot}: couldn't email a code to {User}", bot.Name, user.Id);
            await interaction.FollowupAsync("Sorry - I couldn't send the email just now. Try **Resend code** in a minute, or tell an admin.",
                ephemeral: true, components: CodeButtons());
            return;
        }
        await interaction.FollowupAsync($"I've emailed a 6-digit code to **{address}** - check your spam folder if it doesn't show up. " +
            $"Press **Enter code** (or type `/verify` and the code) within {VerificationCodes.Lifetime.TotalMinutes:0} minutes.",
            ephemeral: true, components: CodeButtons());
    }

    private static MessageComponent CodeButtons() => new ComponentBuilder()
        .WithButton("Enter code", $"{Introduce}:code", ButtonStyle.Primary)
        .WithButton("Resend code", $"{Introduce}:resend", ButtonStyle.Secondary)
        .Build();

    private async Task<string> CheckCodeAsync(SocketGuildUser user, string? typed, BotContext bot)
    {
        var (result, address) = codes.Try(user.Guild.Id, user.Id, typed);
        switch (result)
        {
            case VerificationCodes.Check.NoCode: return "There's no code waiting for you - use `/introduce` to get one.";
            case VerificationCodes.Check.Expired: return "That code has expired - use `/introduce` (or **Resend code**) to get a new one.";
            case VerificationCodes.Check.TooManyTries: return "Too many wrong tries - ask for a new code with **Resend code** or `/introduce`.";
            case VerificationCodes.Check.Wrong: return "That's not the code - check the email and try again.";
        }
        await directories.UseAsync(DirectoryId(user), d => (0, d.MarkVerified(user.Id, address!, DateTime.UtcNow)));
        var note = await GrantRoleAsync(user, Read(bot), bot);
        return $"Verified - {address} is yours. ✅" + (note is null ? "" : "\n" + note);
    }

    // Gives the verified role, if there is one; a note when it says something.
    private static async Task<string?> GrantRoleAsync(SocketGuildUser user, Settings settings, BotContext bot)
    {
        if (settings.VerifiedRoleId is not { } roleId || user.Roles.Any(r => r.Id == roleId)) return null;
        var role = user.Guild.GetRole(roleId);
        if (role is null)
        {
            bot.Logger.LogWarning("Bot {Bot}: verified role {Role} isn't in server {Guild}", bot.Name, roleId, user.Guild.Id);
            return null;
        }
        try
        {
            await user.AddRoleAsync(role);
            return $"You now have the **{role.Name}** role.";
        }
        catch (HttpException ex)
        {
            bot.Logger.LogWarning(ex, "Bot {Bot}: couldn't give {User} the {Role} role", bot.Name, user.Id, role.Name);
            return $"I couldn't give you the **{role.Name}** role - an admin needs to give my role *Manage Roles* and move it above {role.Name}.";
        }
    }

    private async Task WhoIsAsync(SocketSlashCommand command, SocketGuildUser asker, BotContext bot)
    {
        var settings = Read(bot);
        if (!(asker.GuildPermissions.ManageNicknames || asker.GuildPermissions.ManageGuild || asker.Roles.Any(r => settings.ManagerRoleIds.Contains(r.Id))))
        {
            await command.RespondAsync("Only managers can look people up.", ephemeral: true);
            return;
        }
        var target = (IUser)command.Data.Options.First().Value;
        var info = await directories.UseAsync(DirectoryId(asker), d => (d.Find(target.Id), false));
        if (info is null)
        {
            await command.RespondAsync($"<@{target.Id}> hasn't introduced themselves yet.", ephemeral: true, allowedMentions: AllowedMentions.None);
            return;
        }
        var emailLine = info.Email is null ? "none given"
            : info.Email == info.VerifiedEmail ? $"{info.Email} ✅ verified {info.VerifiedAt:yyyy-MM-dd}"
            : info.VerifiedEmail is null ? $"{info.Email} (not verified)"
            : $"{info.Email} (not verified; {info.VerifiedEmail} was, {info.VerifiedAt:yyyy-MM-dd})";
        var embed = new EmbedBuilder()
            .WithTitle(info.RealName)
            .AddField("Discord", $"<@{info.UserId}> ({info.Username})")
            .AddField("Email", emailLine)
            .WithFooter($"Updated {info.UpdatedAt:yyyy-MM-dd}" + (directories.Durable ? "" : " - not saved anywhere yet, resets on restart"));
        await command.RespondAsync(embed: embed.Build(), ephemeral: true);
    }
}
