using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
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
            if (ImGui.Button("Start Client##start_client", new Vector2(-1, 25)) && !running)
            {
                this.plugin.Start();
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
