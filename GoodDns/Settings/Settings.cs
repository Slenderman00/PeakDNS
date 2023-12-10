namespace GoodDns
{
    public class Settings {
        Dictionary<string, Dictionary<string, string>> settings = new Dictionary<string, Dictionary<string, string>>();

        public Settings() {
            LoadSettings();
        }

        private void LoadSettings() {
            //load the settings
            try {
                string[] lines = File.ReadAllLines("./settings.ini");
                string currentSection = "";
                foreach(string line in lines) {
                    if(line.StartsWith("#")) continue;
                    if(line.StartsWith("[")) {
                        currentSection = line.Substring(1, line.Length - 2);
                        settings[currentSection] = new Dictionary<string, string>();
                        continue;
                    }
                    string[] parts = line.Split("=");
                    if(parts.Length != 2) continue;
                    Console.WriteLine($"Setting {currentSection}.{parts[0]} to {parts[1]}");
                    settings[currentSection][parts[0]] = parts[1];
                }
            } catch(Exception e) {
                Console.WriteLine($"Failed to load settings: {e.Message}");
            }
        }

        public string GetSetting(string sectionName, string settingName, string stdValue = "") {
            //get a setting
            if(settings.ContainsKey(sectionName)) {
                if(settings[sectionName].ContainsKey(settingName)) {
                    return settings[sectionName][settingName];
                }
            }
            return stdValue;
        }
    }
}