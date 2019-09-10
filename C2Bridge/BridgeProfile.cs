using System.IO;
using System.Collections.Generic;
using YamlDotNet.Serialization;

namespace C2Bridge
{
    public enum ProfileType
    {
        Bridge
    }

    public class ProfileData
    {
        public string Guid { get; set; }
        public string Data { get; set; }
    }

    public class Profile
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public ProfileType Type { get; set; }
        public string MessageTransform { get; set; }
    }

    public class BridgeProfile : Profile
    {
        public string ReadFormat { get; set; } = "{0},{1}";
        public string WriteFormat { get; set; } = "{0},{1}";
        public string BridgeMessengerCode { get; set; }

        public BridgeProfile()
        {
            this.Type = ProfileType.Bridge;
        }

        public string FormatRead(ProfileData data)
        {
            return string.Format(this.ReadFormat, data.Data, data.Guid);
        }

        public ProfileData ParseRead(string read)
        {
            return ParseFormat(read, this.ReadFormat);
        }

        public ProfileData ParseWrite(string write)
        {
            return ParseFormat(write, this.WriteFormat);
        }

        private ProfileData ParseFormat(string info, string format)
        {
            List<string> parsed = Utilities.Parse(info, format);
            if (parsed.Count == 2)
            {
                return new ProfileData { Data = parsed[0], Guid = parsed[1] };
            }
            return null;
        }

        public static BridgeProfile Create(string ProfileFilePath)
        {
            using (TextReader reader = File.OpenText(ProfileFilePath))
            {
                var deserializer = new DeserializerBuilder().Build();
                BridgeProfileYaml yaml = deserializer.Deserialize<BridgeProfileYaml>(reader);
                return CreateFromBridgeProfileYaml(yaml);
            }
        }

        private class BridgeProfileYaml
        {
            public string Name { get; set; } = "";
            public string Description { get; set; } = "";
            public string MessageTransform { get; set; } = "";
            public string ReadFormat { get; set; } = "";
            public string WriteFormat { get; set; } = "";
            public string BridgeMessengerCode { get; set; } = "";
        }

        private static BridgeProfile CreateFromBridgeProfileYaml(BridgeProfileYaml yaml)
        {
            return new BridgeProfile
            {
                Name = yaml.Name,
                Description = yaml.Description,
                MessageTransform = yaml.MessageTransform,
                ReadFormat = yaml.ReadFormat.TrimEnd('\n').Replace("{DATA}", "{0}").Replace("{GUID}", "{1}"),
                WriteFormat = yaml.WriteFormat.TrimEnd('\n').Replace("{DATA}", "{0}").Replace("{GUID}", "{1}"),
                BridgeMessengerCode = yaml.BridgeMessengerCode.TrimEnd('\n')
            };
        }
    }
}
