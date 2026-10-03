using Eidolon.Core.Application;
using Eidolon.App.Localization;
using Eidolon.Core.Infrastructure;
using Eidolon.Core.Domain;
using System.Text.Json;

namespace Eidolon.App.Services
{
    public class SettingsStore
    {
        private readonly StringHelper _strings;
        private readonly AtomicJsonFile _json;
        private readonly string _path;
        private readonly object _gate = new object();

        public SettingsStore(string dataDirectory, AtomicJsonFile json, StringHelper strings)
        {
            _strings = strings;
            _json = json;
            _path = Path.Combine(dataDirectory, "Settings.json");
        }

        public DesktopSettings Load()
        {
            lock (_gate)
            {
                DesktopSettings settings = _json.Read<DesktopSettings>(_path);
                if (settings == null)
                {
                    settings = new DesktopSettings();
                }
                if (settings.SchemaVersion != 1)
                {
                    throw new InvalidDataException(_strings.GetString("EidolonText100"));
                }
                MigrateServerAddress(settings);
                settings.Validate();
                return settings;
            }
        }

        public void Save(DesktopSettings settings)
        {
            settings.Validate();
            lock (_gate)
            {
                _json.Write(_path, settings);
            }
        }

        private void MigrateServerAddress(DesktopSettings settings)
        {
            bool hasMode = settings.AdditionalSettings.TryGetValue("UseExternalServer", out JsonElement mode);
            bool hasPort = settings.AdditionalSettings.TryGetValue("EnginePort", out JsonElement port);
            bool usedExternalServer = false;
            if (hasMode == true)
            {
                if (mode.ValueKind != JsonValueKind.True && mode.ValueKind != JsonValueKind.False)
                {
                    throw new StudioException(StudioMessageCode.InvalidServerAddress);
                }
                usedExternalServer = mode.GetBoolean();
            }
            if (usedExternalServer == false && (hasMode == true || hasPort == true))
            {
                settings.ServerAddress = StudioSettings.DefaultServerAddress;
                if (hasPort == true)
                {
                    if (port.ValueKind != JsonValueKind.Number || port.TryGetInt32(out int enginePort) == false)
                    {
                        throw new StudioException(StudioMessageCode.InvalidEnginePort);
                    }
                    if (enginePort < 1024 || enginePort > 65535)
                    {
                        throw new StudioException(StudioMessageCode.InvalidEnginePort);
                    }
                    settings.ServerAddress = "http://127.0.0.1:" + enginePort;
                }
            }
            settings.AdditionalSettings.Remove("UseExternalServer");
            settings.AdditionalSettings.Remove("EnginePort");
        }
    }

}
