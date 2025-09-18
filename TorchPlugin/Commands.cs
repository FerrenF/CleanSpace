using CleanSpaceShared.Config;
using CleanSpaceShared.Plugin;
using Torch.Commands;
using Torch.Commands.Permissions;
using VRage.Game.ModAPI;

namespace CleanSpaceTorch
{
    public class Commands : CommandModule
    {
        private static IPluginConfig Config => Common.Config;

        private void Respond(string message)
        {
            Context?.Respond(message);
        }

        // TODO: Replace cmd with the name of your chat command
        // TODO: Implement subcommands as needed
        private void RespondWithHelp()
        {
            Respond("PluginDetector commands:");
        }

        private void RespondWithInfo()
        {
            var config = CleanSpaceTorchPlugin.Instance.Config;
            Respond($"{CleanSpaceTorchPlugin.PluginName} plugin is enabled: {Format(config.Enabled)}");
        }

        // Custom formatters

        private static string Format(bool value) => value ? "Yes" : "No";

        // Custom parsers

        private static bool TryParseBool(string text, out bool result)
        {
            switch (text.ToLower())
            {
                case "1":
                case "on":
                case "yes":
                case "y":
                case "true":
                case "t":
                    result = true;
                    return true;

                case "0":
                case "off":
                case "no":
                case "n":
                case "false":
                case "f":
                    result = false;
                    return true;
            }

            result = false;
            return false;
        }

        // ReSharper disable once UnusedMember.Global

        [Command("cmd help", "PluginDetector: Help")]
        [Permission(MyPromoteLevel.None)]
        public void Help()
        {
            RespondWithHelp();
        }

        // ReSharper disable once UnusedMember.Global
        [Command("cmd info", "PluginDetector: Prints the current settings")]
        [Permission(MyPromoteLevel.None)]
        public void Info()
        {
            RespondWithInfo();
        }

        // ReSharper disable once UnusedMember.Global
        [Command("cmd enable", "PluginDetector: Enables the plugin")]
        [Permission(MyPromoteLevel.Admin)]
        public void Enable()
        {
            Config.Enabled = true;
            RespondWithInfo();
        }

        // ReSharper disable once UnusedMember.Global
        [Command("cmd disable", "PluginDetector: Disables the plugin")]
        [Permission(MyPromoteLevel.Admin)]
        public void Disable()
        {
            Config.Enabled = false;
            RespondWithInfo();
        }

        // TODO: Subcommand
        // ReSharper disable once UnusedMember.Global
        [Command("cmd subcmd", "PluginDetector: TODO: Subcommand")]
        [Permission(MyPromoteLevel.Admin)]
        public void SubCmd(string name, string value)
        {
            // TODO: Process command parameters (for example name and value)

            RespondWithInfo();
        }
    }
}