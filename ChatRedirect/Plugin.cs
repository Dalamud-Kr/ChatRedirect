using System;
using System.Collections.Concurrent;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Game;
using Dalamud.Game.Command;
using Dalamud.Game.Gui;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Logging;
using Dalamud.Plugin;
using Newtonsoft.Json;
using TinyIpc.Messaging;
using XivCommon;
using XivCommon.Functions;

namespace ChatRedirect
{
    public class Plugin : IDalamudPlugin
    {
        public string Name => "ChatRedirect";

        private const string CommandName = "/chatrediect";
        private const int Port = 61801;

        private const string MessageBus = "ChatRedirect__4F87BD8E-C064-4E70-92D2-78386C5CFCB0";

        [PluginService] public static DalamudPluginInterface PluginInterface { get; private set; }
        [PluginService] public static Framework Framework { get; set; }
        [PluginService] public static ChatGui ChatGui { get; set; }
        [PluginService] public static GameGui GameGui { get; set; }
        [PluginService] public static SigScanner SigScanner { get; set; }
        [PluginService] public static CommandManager CommandManager { get; set; }

        //private readonly BuddyCommandManager buddyCommandManager;
        private readonly XivCommonBase common;

        private readonly TinyMessageBus messageBus;

        internal PluginUI PluginUI { get; }

        internal WindowSystem WindowSystem { get; } = new("ChatRedirect");

        private readonly ManualResetEventSlim working = new();
        private readonly int pid;
        private bool serverMode;

        private readonly ConcurrentQueue<Data> strQueue = new();

        public Plugin()
        {
            try
            {
#pragma warning disable CS8602
                this.pid = Environment.ProcessId;

                this.messageBus = new TinyMessageBus(MessageBus);

                Framework.Update += this.Framework_Update;

                CommandManager.AddHandler(CommandName, new CommandInfo(this.OnCommand));

                this.PluginUI = new PluginUI(this);

                this.WindowSystem.AddWindow(this.PluginUI);

                PluginInterface.UiBuilder.OpenConfigUi += this.UiBuilder_OpenConfigUi;
                PluginInterface.UiBuilder.Draw += this.UiBuilder_Draw;

                //this.buddyCommandManager = new(GameGui, SigScanner);
                this.common = new XivCommonBase();
#pragma warning restore CS8602
            }
            catch (Exception ex)
            {
                PluginLog.LogError(ex, "Dalacraft");
                this.Dispose(true);
            }
        }

        ~Plugin()
        {
            this.Dispose(false);
        }

        public void Dispose()
        {
            this.Dispose(true);
            GC.SuppressFinalize(this);
        }
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                this.Stop();

                this.working.Dispose();
                try
                {
                    this.messageBus?.Dispose();
                }
                catch
                {
                }

                PluginInterface.UiBuilder.Draw -= this.UiBuilder_Draw;
                PluginInterface.UiBuilder.OpenConfigUi -= this.UiBuilder_OpenConfigUi;
                this.WindowSystem.RemoveAllWindows();

                this.PluginUI?.Dispose();

                CommandManager.RemoveHandler(CommandName);
            }
        }

        private void UiBuilder_OpenConfigUi()
        {
            this.PluginUI.IsOpen = true;
        }
        private void UiBuilder_Draw()
        {
            try
            {
                this.WindowSystem.Draw();
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, $"{0}.{1}", nameof(Plugin), nameof(UiBuilder_Draw));
            }
        }

        private void OnCommand(string command, string arguments)
        {
            this.PluginUI.IsOpen = true;
        }

        public bool Running => this.working.IsSet;

        public void Stop()
        {
            if (!this.working.IsSet) return;
            this.working.Reset();

            try
            {
                this.messageBus.MessageReceived -= this.MessageBus_MessageReceived;
                ChatGui.ChatMessage -= this.ChatGui_ChatMessage;

                ChatGui.PrintChat(new XivChatEntry
                {
                    Type = XivChatType.Urgent,
                    Message = "ChatRedirect Disabled",
                });
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, "Stop");
            }
        }

        public void Start(bool senderMode)
        {
            if (this.working.IsSet) return;
            this.working.Set();

            try
            {
                this.serverMode = senderMode;

                this.messageBus.MessageReceived += this.MessageBus_MessageReceived;
                ChatGui.ChatMessage += this.ChatGui_ChatMessage;

                ChatGui.PrintChat(new XivChatEntry
                {
                    Type    = XivChatType.Urgent,
                    Message = $"ChatRedirect Enabled ({(senderMode ? "Sender" : "Receiver")})",
                });
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, "StartSender");

                this.Stop();
            }
        }

        private class Data
        {
            [JsonProperty("p")] public int         Pid        { get; set; }
            [JsonProperty("t")] public XivChatType ChatType   { get; set; }
            [JsonProperty("i")] public uint        SenderID   { get; set; }
#pragma warning disable CS8618
            [JsonProperty("s")] public string      SenderName { get; set; }
            [JsonProperty("m")] public string      Message    { get; set; }
#pragma warning restore CS8618
        }
        private void Framework_Update(Framework framework)
        {
            while (this.strQueue.TryDequeue(out var chatData))
            {
                try
                {
                    var str = Encoding.UTF8.GetString(Convert.FromBase64String(chatData.Message));
                    PluginLog.Verbose(str);
                    this.common.Functions.Chat.SendMessage($"/fc {str}");
                }
                catch
                {
                }
            }
        }
        private void ChatGui_ChatMessage(XivChatType type, uint senderId, ref SeString sender, ref SeString message, ref bool isHandled)
        {
            PluginLog.Verbose("ChatGui_ChatMessage");
            try
            {
                switch (type)
                {
                case XivChatType.CrossLinkShell1:
                    isHandled = true;

                    if (this.serverMode) return;
                    this.SendMessage(type, senderId, sender, message);

                    break;

                case XivChatType.FreeCompany:
                case XivChatType.Ls1:
                case XivChatType.Ls2:
                case XivChatType.Ls3:
                case XivChatType.Ls4:
                case XivChatType.Ls5:
                case XivChatType.Ls6:
                case XivChatType.Ls7:
                case XivChatType.Ls8:
                    if (!this.serverMode) return;

                    this.SendMessage(type, senderId, sender, message);

                    break;
                }
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, "ChatGui_ChatMessage");
            }
        }

        private void SendMessage(XivChatType type, uint senderId, SeString sender, SeString message)
        {
            byte[] senderData, messageData;

            try
            {
                senderData = sender.Encode();
            }
            catch
            {
                senderData = Encoding.UTF8.GetBytes(sender.ToString());
            }

            if (this.serverMode)
            {
                messageData = Encoding.UTF8.GetBytes(message.ToString());
            }
            else
            {
                try
                {
                    messageData = message.Encode();
                }
                catch
                {
                    messageData = Encoding.UTF8.GetBytes(message.ToString());
                }
            }

            Task.Factory.StartNew(() =>
            {
                try
                {
                    var data = new Data
                    {
                        Pid        = this.pid,
                        ChatType   = type,
                        SenderID   = senderId,
                        SenderName = Convert.ToBase64String(senderData),
                        Message    = Convert.ToBase64String(messageData),
                    };

                    this.messageBus.PublishAsync(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(data)));
                }
                catch (Exception ex)
                {
                    PluginLog.Error(ex, "PublishAsync");
                }
            });
        }

        private void MessageBus_MessageReceived(object? sender, TinyMessageReceivedEventArgs e)
        {
            PluginLog.Verbose("MessageBus_MessageReceived");
            try
            {
                var str = Encoding.UTF8.GetString(e.Message);
                PluginLog.Verbose(str);

                var chatData = JsonConvert.DeserializeObject<Data>(str);
                if (chatData != null)
                {
                    if (this.serverMode)
                    {
                        // this.buddyCommandManager.Execute($"/fc {Convert.FromBase64String(chatData.Message)}");
                        // this.common.Functions.Chat.SendMessage($"/fc {Convert.FromBase64String(chatData.Message)}");

                        strQueue.Enqueue(chatData);
                    }
                    else
                    {
                        ChatGui.PrintChat(new XivChatEntry
                        {
                            Type = chatData.ChatType,
                            SenderId = chatData.SenderID,
                            Name = SeString.Parse(Convert.FromBase64String(chatData.SenderName)),
                            Message = SeString.Parse(Convert.FromBase64String(chatData.Message)),
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, "PipeClient_ReadCallback");
            }
        }
    }
}
