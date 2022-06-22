using System;
using System.Numerics;
using Dalamud.Interface.Windowing;
using ImGuiNET;

namespace ChatRedirect
{
    internal class PluginUI : Window, IDisposable
    {
        protected readonly Plugin plugin;

        public PluginUI(Plugin plugin) : base("ChatRedirect")
        {
            this.plugin = plugin;

            this.Size = new Vector2(300, 200);
            this.SizeCondition = ImGuiCond.Appearing;
        }
        ~PluginUI()
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
            }
        }

        public override void Draw()
        {
            ImGui.PushItemWidth(-1);

            var running = this.plugin.Running;

            if (running) ImGui.PushStyleVar(ImGuiStyleVar.Alpha, 0.2f);
            if (ImGui.Button("Start Sender##start_sender", new Vector2(-1, 25)) && !running)
            {
                this.plugin.Start(true);
            }
            if (running) ImGui.PopStyleVar();

            if (running) ImGui.PushStyleVar(ImGuiStyleVar.Alpha, 0.2f);
            if (ImGui.Button("Start Receiver##start_Receiver", new Vector2(-1, 25)) && !running)
            {
                this.plugin.Start(false);
            }
            if (running) ImGui.PopStyleVar();

            if (!running) ImGui.PushStyleVar(ImGuiStyleVar.Alpha, 0.2f);
            if (ImGui.Button("Stop##stop", new Vector2(-1, 25)) && running)
            {
                this.plugin.Stop();
            }
            if (!running) ImGui.PopStyleVar();
        }
    }
}
