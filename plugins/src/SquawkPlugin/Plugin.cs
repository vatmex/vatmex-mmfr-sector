using System;
using System.Collections.Concurrent;
using System.ComponentModel.Composition;
using System.Text.RegularExpressions;
using vatsys;
using vatsys.Plugin;

namespace MMFRVatsys.CustomLabels
{
    [Export(typeof(IPlugin))]
    public class RnvCapLabelItem : ILabelPlugin
    {
        private const string LABEL_ITEM = "LABEL_ITEM_RNVCAP";

        private readonly ConcurrentDictionary<string, char> pbnValues =
            new ConcurrentDictionary<string, char>();

        public string Name => "MMFR RNVCAP";

        public void OnFDRUpdate(FDP2.FDR updated)
        {
            if (FDP2.GetFDRIndex(updated.Callsign) == -1)
            {
                char removed;
                pbnValues.TryRemove(updated.Callsign, out removed);
                return;
            }

            Match match = Regex.Match(updated.Remarks, "PBN\\/\\w+\\s");
            bool hasA = Regex.IsMatch(match.Value, "A\\d");
            bool hasB = Regex.IsMatch(match.Value, "B\\d");
            bool hasL = Regex.IsMatch(match.Value, "L\\d");
            bool hasRnp2 = updated.Remarks.Contains("NAV/RNP2")
                        || updated.Remarks.Contains("NAV/GLS RNP2");

            char cap = 'P';
            if (hasRnp2 || hasL || hasB || hasA)
                cap = '\0';

            pbnValues.AddOrUpdate(updated.Callsign, cap, (k, v) => cap);
        }

        public void OnRadarTrackUpdate(RDP.RadarTrack updated)
        {
        }

        public CustomLabelItem GetCustomLabelItem(string itemType, Track track,
            FDP2.FDR flightDataRecord, RDP.RadarTrack radarTrack)
        {
            if (flightDataRecord == null) return null;
            if (itemType != LABEL_ITEM) return null;

            char value;
            pbnValues.TryGetValue(flightDataRecord.Callsign, out value);

            return new CustomLabelItem
            {
                Type = itemType,
                ForeColourIdentity = Colours.Identities.Default,
                Text = value.ToString()
            };
        }

        public CustomColour SelectASDTrackColour(Track track)
        {
            return null;
        }

        public CustomColour SelectGroundTrackColour(Track track)
        {
            return null;
        }
    }

    [Export(typeof(IPlugin))]
    public class SSRStripItem : IStripPlugin
    {
        private const string STRIP_ITEM = "AssignedSSRMX";

        private readonly ConcurrentDictionary<string, bool> ssrCodeValues =
            new ConcurrentDictionary<string, bool>();

        public string Name => "MMFR Squawk Validation";

        public void OnFDRUpdate(FDP2.FDR updated)
        {
        }

        public void OnRadarTrackUpdate(RDP.RadarTrack updated)
        {
            if (updated.CoupledFDR == null) return;

            if (FDP2.GetFDRIndex(updated.CoupledFDR.Callsign) == -1)
            {
                bool removed;
                ssrCodeValues.TryRemove(updated.CoupledFDR.Callsign, out removed);
                return;
            }

            bool codeValidity = IsSSRCodeValid(updated.CoupledFDR, updated.ActualAircraft);
            ssrCodeValues.AddOrUpdate(updated.CoupledFDR.Callsign, codeValidity,
                (k, v) => codeValidity);
        }

        public CustomStripItem GetCustomStripItem(string itemType, Track track,
            FDP2.FDR flightDataRecord, RDP.RadarTrack radarTrack)
        {
            if (flightDataRecord == null) return null;
            if (itemType != STRIP_ITEM) return null;

            bool valid;
            ssrCodeValues.TryGetValue(flightDataRecord.Callsign, out valid);

            CustomStripItem item = new CustomStripItem
            {
                Type = itemType,
                ForeColourIdentity = Colours.Identities.Default,
                Text = (flightDataRecord.AssignedSSRCode != -1)
                    ? Convert.ToString(flightDataRecord.AssignedSSRCode, 8).PadLeft(4, '0')
                    : "",
                Border = BorderFlags.None
            };

            if (flightDataRecord.AssignedSSRCode != -1 && !valid)
            {
                item.BackColourIdentity = Colours.Identities.Custom;
                item.CustomBackColour = new CustomColour(255, 255, 255);
                item.ForeColourIdentity = Colours.Identities.ASDBackground;
            }

            return item;
        }

        private bool IsSSRCodeValid(FDP2.FDR flightDataRecord, NetworkPilot actualAircraft)
        {
            switch (actualAircraft.TransponderCode)
            {
                case 640:   // 1200 - VFR
                case 1024:  // 2000 - non-discrete
                case 3904:  // 7500 - unlawful interference
                case 3968:  // 7600 - radio failure
                case 4032:  // 7700 - emergency
                    return true;
            }

            return actualAircraft.TransponderCode == flightDataRecord.AssignedSSRCode;
        }
    }
}
