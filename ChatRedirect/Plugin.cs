using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Game.Command;
using Dalamud.Game.Gui;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Logging;
using Dalamud.Plugin;
using Newtonsoft.Json;

namespace ChatRedirect
{
    public class Plugin : IDalamudPlugin
    {
        public string Name => "ChatRedirect";

        private const string CommandName = "/chatrediect";
        private const int Port = 61801;

        [PluginService] public DalamudPluginInterface PluginInterface { get; private set; }
        [PluginService] public ChatGui ChatGui { get; set; }
        [PluginService] public CommandManager CommandManager { get; set; }

        private readonly UdpClient udpClient;

        internal PluginUI PluginUI { get; }

        internal WindowSystem WindowSystem { get; } = new("ChatRedirect");

        private readonly ManualResetEventSlim working = new();
        private readonly int pid;

        public Plugin()
        {
            try
            {
#pragma warning disable CS8602
                this.pid = Process.GetCurrentProcess().Id;

                this.udpClient = new UdpClient(new IPEndPoint(IPAddress.Loopback, Port));

                this.CommandManager.AddHandler(CommandName, new CommandInfo(this.OnCommand));

                this.PluginUI = new PluginUI(this);

                this.WindowSystem.AddWindow(this.PluginUI);

                this.PluginInterface.UiBuilder.OpenConfigUi += this.UiBuilder_OpenConfigUi;
                this.PluginInterface.UiBuilder.Draw += this.UiBuilder_Draw;
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
                this.udpClient?.Dispose();

                this.PluginInterface.UiBuilder.Draw -= this.UiBuilder_Draw;
                this.PluginInterface.UiBuilder.OpenConfigUi -= this.UiBuilder_OpenConfigUi;
                this.WindowSystem.RemoveAllWindows();

                this.PluginUI?.Dispose();

                this.CommandManager.RemoveHandler(CommandName);
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

            this.udpClient?.Close();

            this.ChatGui.ChatMessage -= this.ChatGui_ChatMessage;

            this.ChatGui.PrintChat(new XivChatEntry
            {
                Type = XivChatType.Urgent,
                Message = "ChatRedirect Disabled",
            });
        }

        public void Start()
        {
            if (this.working.IsSet) return;
            this.working.Set();

            try
            {
                this.udpClient.BeginReceive(this.ReceiveCallback, this.udpClient);

                this.ChatGui.ChatMessage += this.ChatGui_ChatMessage;
                this.ChatGui.PrintChat(new XivChatEntry
                {
                    Type    = XivChatType.Urgent,
                    Message = "ChatRedirect Enabled",
                });
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, "error");

                this.Stop();
            }
        }

        private class Data
        {
            [JsonProperty("p")] public int Pid { get; set; }
            [JsonProperty("t")] public XivChatType ChatType { get; set; }
            [JsonProperty("i")] public uint SenderID { get; set; }
#pragma warning disable CS8618
            [JsonProperty("s")] public string SenderName { get; set; }
            [JsonProperty("m")] public string Message { get; set; }
#pragma warning restore CS8618
        }
        private void ChatGui_ChatMessage(XivChatType type, uint senderId, ref SeString sender, ref SeString message, ref bool isHandled)
        {
            switch (type)
            {
            case XivChatType.FreeCompany:
            case XivChatType.Ls1:
            case XivChatType.Ls2:
            case XivChatType.Ls3:
            case XivChatType.Ls4:
            case XivChatType.Ls5:
            case XivChatType.Ls6:
            case XivChatType.Ls7:
            case XivChatType.Ls8:
                var data = new Data
                {
                    Pid        = this.pid,
                    ChatType   = type,
                    SenderID   = senderId,
                    SenderName = (string)sender.ToString().Clone(),
                    Message    = (string)message.ToString().Clone(),
                };

                Task.Factory.StartNew(() =>
                {
                    try
                    {
                        using var s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                        s.SendTo(
                            Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(data)),
                            new IPEndPoint(IPAddress.Loopback, Port)
                        );
                    }
                    catch (Exception ex)
                    {
                        PluginLog.Error(ex, "ChatGui_ChatMessage");
                    }
                });

                break;
            }
        }

        private void ReceiveCallback(IAsyncResult ar)
        {
            try
            {
                var udpClient = ar.AsyncState as UdpClient;
                if (udpClient == null) return;

                try
                {
                    var remoteEP = new IPEndPoint(IPAddress.Any, Port);
                    var bytes = udpClient.EndReceive(ar, ref remoteEP);

                    var str = Encoding.UTF8.GetString(bytes);
                    PluginLog.Verbose(str);

                    var chatData = JsonConvert.DeserializeObject<Data>(str);
                    if (chatData != null && chatData.Pid != pid)
                    {
                        this.ChatGui.PrintChat(new XivChatEntry
                        {
                            Type = chatData.ChatType,
                            SenderId = chatData.SenderID,
                            Name = chatData.SenderName,
                            Message = chatData.Message,
                        });
                    }
                }
                catch
                {
                }

                this.udpClient.BeginReceive(this.ReceiveCallback, this.udpClient);
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, "PipeClient_ReadCallback");
            }
        }
    }
}
