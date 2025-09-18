using CleanSpaceShared.Config;
using CleanSpaceShared.Struct;
using System;
using System.Collections.ObjectModel;
using System.Xml.Serialization;
using Torch;
using Torch.Views;

namespace CleanSpaceTorch
{
    [Serializable]
    public class ViewModelConfig : ViewModel, IPluginConfig
    {
        private long tokenValidTimeTicks = TimeSpan.TicksPerSecond;
        private bool enabled = true;
        private bool detectCodeChanges = true;
        private string secret = "secret";

        ObservableCollection<string> selectedPlugins = new ObservableCollection<string>();
        ObservableCollection<PluginListEntry> analyzedPlugins = new ObservableCollection<PluginListEntry>();

        PluginListType pluginListType = PluginListType.Blacklist;
        ListMatchAction listMatchAction = ListMatchAction.Deny;

        [XmlIgnore]
        public string[] PluginListTypes
        {
            get { return Enum.GetNames(typeof(PluginListType));}
        }
        [XmlIgnore]
        public string[] ListMatchActions
        {
            get { return Enum.GetNames(typeof(ListMatchAction)); }
        }

        public long TokenValidTimeSeconds { get => tokenValidTimeTicks; set => SetValue(ref tokenValidTimeTicks, value); }

        [Display(Order = 1, GroupName = "General", Name = "Enable plugin", Description = "Enable the plugin")]
        public bool Enabled
        {
            get => enabled;
            set => SetValue(ref enabled, value);
        }

        [Display(Order = 2, GroupName = "General", Name = "Detect code changes", Description = "Disable the plugin if any changes to the game code are detected before patching")]
        public bool DetectCodeChanges
        {
            get => detectCodeChanges;
            set => SetValue(ref detectCodeChanges, value);
        }


        [Display(Order = 1, GroupName = "Security", Name = "Secret Key", Description = "This secret key is used in generating validation responses. Make it unique.")] 
        public string Secret
        {
            get => secret;
            set => SetValue(ref secret, value);
        }
     

        [Display(Order = 1, GroupName = "Plugins", Name = "Analyzed Plugins", Description = "This list displays the plugins that have been analyzed by Clean Space in this torch instance.")]
        public ObservableCollection<PluginListEntry> AnalyzedPlugins 
        {
            get => analyzedPlugins;              
            set => SetValue(ref analyzedPlugins, value);
        }

     
        public ObservableCollection<string> SelectedPlugins 
        {
            get => selectedPlugins;
            set => SetValue(ref selectedPlugins, value);
        }


        [Display(Order = 2, GroupName = "Security", Name = "Plugin List Type", Description = "Whitelist or Blacklist")]
        public PluginListType PluginListType 
        { 
            get => pluginListType; 
            set => SetValue(ref pluginListType, value);
        }


        [Display(Order = 3, GroupName = "Security", Name = "List Match Action", Description = "What to do if a connecting user has a plugin with a matching hash?")]
        public ListMatchAction ListMatchAction 
        {
            get => listMatchAction; 
            set => SetValue(ref listMatchAction, value);
        }
    }
}