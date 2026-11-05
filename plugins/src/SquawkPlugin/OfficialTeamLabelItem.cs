// VATMEX / WorldFlight official teams: callsign-only identification.
// This class is compiled into the existing MMFRVatsys.CustomLabels assembly.
// It does not change RNVCAP, squawk validation, Tracks.xml, or OzStrips.
// The roster is loaded once at vatSys startup; restart vatSys after editing the CSV.
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using vatsys;
using vatsys.Plugin;

namespace MMFRVatsys.CustomLabels
{
    [Export(typeof(IPlugin))]
    public sealed class OfficialTeamLabelItem : ILabelPlugin
    {
        private const string LabelItem = "LABEL_ITEM_OFFICIAL_TEAM";
        private readonly HashSet<string> teamCallsigns;

        public string Name => "MMFR Official Teams";

        public OfficialTeamLabelItem()
        {
            teamCallsigns = ReadTeamCallsigns();
        }

        public void OnFDRUpdate(FDP2.FDR updated) { }
        public void OnRadarTrackUpdate(RDP.RadarTrack updated) { }

        public CustomLabelItem GetCustomLabelItem(string itemType, Track track,
            FDP2.FDR flightDataRecord, RDP.RadarTrack radarTrack)
        {
            if (itemType != LabelItem || flightDataRecord == null ||
                !teamCallsigns.Contains(flightDataRecord.Callsign ?? string.Empty))
                return null;

            return new CustomLabelItem
            {
                Type = itemType,
                Text = "WF",
                ForeColourIdentity = Colours.Identities.Custom,
                CustomForeColour = new CustomColour(245, 202, 40)
            };
        }

        public CustomColour SelectASDTrackColour(Track track) { return null; }
        public CustomColour SelectGroundTrackColour(Track track) { return null; }

        private static HashSet<string> ReadTeamCallsigns()
        {
            var callsigns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                string folder = Path.GetDirectoryName(typeof(OfficialTeamLabelItem).Assembly.Location);
                string filePath = Path.Combine(folder, "OfficialTeams.csv");
                if (!File.Exists(filePath)) return callsigns;

                foreach (string originalLine in File.ReadAllLines(filePath))
                {
                    string line = originalLine.Trim().TrimStart('\uFEFF');
                    if (line.Length == 0 || line.StartsWith("#")) continue;

                    string[] fields = line.Split(',');
                    string callsign = fields[0].Trim();
                    if (callsign.Length == 0 ||
                        callsign.Equals("callsign", StringComparison.OrdinalIgnoreCase)) continue;

                    // New format: callsign,enabled
                    // Old format: callsign,cid,enabled (CID is deliberately ignored)
                    string enabled = fields.Length >= 3 ? fields[2].Trim() :
                                     fields.Length >= 2 ? fields[1].Trim() : "true";
                    if (enabled.Equals("false", StringComparison.OrdinalIgnoreCase) ||
                        enabled == "0") continue;
                    callsigns.Add(callsign);
                }
            }
            catch (Exception)
            {
                // CSV errors must not prevent vatSys from loading.
                // Fail closed: do not show any WorldFlight markers.
                callsigns.Clear();
            }
            return callsigns;
        }
    }
}
