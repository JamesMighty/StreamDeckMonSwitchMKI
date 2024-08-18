using System;
using System.Collections.Generic;
using System.Linq;

namespace StreamDeckMonitorSwitch.ddcmon
{
    [Flags]
    public enum VCPPropertyPerms: int
    {
        NOT_SET,
        READ,
        WRITE,
        READ_WRITE = READ | WRITE
    }

    [Serializable]
    public class VCPProperty
    {
        public uint code { get; private set; }
        public string codeName { get; private set; }
        public VCPPropertyPerms perms { get; private set; }

        internal VCPProperty(uint code, string codeName, VCPPropertyPerms perms)
        {
            this.code = code;
            this.codeName = codeName;
            this.perms = perms;
        }

    }

    public class VCPPropertyStatus
    {
        public Dictionary<VCPProperty, int[]> possibleVCPValues = new Dictionary<VCPProperty, int[]>();

    }

    /**
        +----------+-------------------------------------+------------+-------------------------------------+
        | VCP_CODE | VCP_CODE_NAME                       | READ-WRITE |                                     |
        +----------+-------------------------------------+------------+-------------------------------------+
        | 02       | NEW_CONTROL_VALUE                   | READ+WRITE | NEW_CONTROL_VALUE                   |
        | 04       | RESTORE_FACTORY_DEFAULTS            | WRITE_ONLY | RESTORE_FACTORY_DEFAULTS            |
        | 05       | RESTORE_FACTORY_LUMINANCE_CONTRAST  | WRITE_ONLY | RESTORE_FACTORY_LUMINANCE_CONTRAST  |
        | 08       | RESTORE_FACTORY_COLOR_DEFAULTS      | WRITE_ONLY | RESTORE_FACTORY_COLOR_DEFAULTS      |
        | 0B       | COLOR_TEMPERATURE_INCREMENT         | READ_ONLY  | COLOR_TEMPERATURE_INCREMENT         |
        | 0C       | COLOR_TEMPERATURE_REQUEST           | READ+WRITE | COLOR_TEMPERATURE_REQUEST           |
        | 10       | BRIGHTNESS                          | READ+WRITE | BRIGHTNESS                          |
        | 12       | CONTRAST                            | READ+WRITE | CONTRAST                            |
        | 14       | SELECT_COLOR_PRESET                 | READ+WRITE | SELECT_COLOR_PRESET                 |
        | 16       | VIDEO_GAIN_DRIVE_RED                | READ+WRITE | VIDEO_GAIN_DRIVE_RED                |
        | 18       | VIDEO_GAIN_DRIVE_GREEN              | READ+WRITE | VIDEO_GAIN_DRIVE_GREEN              |
        | 1A       | VIDEO_GAIN_DRIVE_BLUE               | READ+WRITE | VIDEO_GAIN_DRIVE_BLUE               |
        | 52       | ACTIVE_CONTROL                      | READ_ONLY  | ACTIVE_CONTROL                      |
        | 60       | INPUT_SELECT                        | READ+WRITE | INPUT_SELECT                        |
        | AC       | HORIZONTAL_FREQUENCY                | READ_ONLY  | HORIZONTAL_FREQUENCY                |
        | AE       | VERTICAL_FREQUENCY                  | READ_ONLY  | VERTICAL_FREQUENCY                  |
        | B2       | FLAT_PANEL_SUB_PIXEL_LAYOUT         | READ_ONLY  | FLAT_PANEL_SUB_PIXEL_LAYOUT         |
        | B6       | DISPLAY_TECHNOLOGY_TYPE             | READ_ONLY  | DISPLAY_TECHNOLOGY_TYPE             |
        | C0       | DISPLAY_USAGE_TIME                  | READ_ONLY  | DISPLAY_USAGE_TIME                  |
        | C6       | APPLICATION_ENABLE_KEY              | READ_ONLY  | APPLICATION_ENABLE_KEY              |
        | C8       | DISPLAY_CONTROLLER_ID               | READ+WRITE | DISPLAY_CONTROLLER_ID               |
        | C9       | DISPLAY_FIRMWARE_LEVEL              | READ_ONLY  | DISPLAY_FIRMWARE_LEVEL              |
        | CA       | OSD                                 | READ+WRITE | OSD                                 |
        | CC       | OSD_LANGUAGE                        | READ+WRITE | OSD_LANGUAGE                        |
        | D6       | POWER_MODE                          | READ+WRITE | POWER_MODE                          |
        +----------+-------------------------------------+------------+-------------------------------------+
      */
    public static class VCP_PROPS
    {
        public static readonly string UNKNOWN_CODE_NAME = "Unknown";

        public static readonly VCPProperty   NEW_CONTROL_VALUE                   = new VCPProperty(0x02, "New Control Value", VCPPropertyPerms.READ) ;
        public static readonly VCPProperty   RESTORE_FACTORY_DEFAULTS            = new VCPProperty(0x04, "Restore Factory Defaults", VCPPropertyPerms.WRITE) ;
        public static readonly VCPProperty   RESTORE_FACTORY_LUMINANCE_CONTRAST  = new VCPProperty(0x05, "Restore Factory Luminance/ Contrast", VCPPropertyPerms.WRITE);
        public static readonly VCPProperty   RESTORE_FACTORY_COLOR_DEFAULTS      = new VCPProperty(0x08, "Restore Factory Color Defaults", VCPPropertyPerms.WRITE);
        public static readonly VCPProperty   COLOR_TEMPERATURE_INCREMENT         = new VCPProperty(0x0B, "Color Temperature Increment", VCPPropertyPerms.READ);
        public static readonly VCPProperty   COLOR_TEMPERATURE_REQUEST           = new VCPProperty(0x0C, "Color Temperature Request", VCPPropertyPerms.READ_WRITE);
        public static readonly VCPProperty   BRIGHTNESS                          = new VCPProperty(0x10, "Brightness", VCPPropertyPerms.READ_WRITE);
        public static readonly VCPProperty   CONTRAST                            = new VCPProperty(0x12, "Contrast", VCPPropertyPerms.READ_WRITE);
        public static readonly VCPProperty   SELECT_COLOR_PRESET                 = new VCPProperty(0x14, "Select Color Preset", VCPPropertyPerms.READ_WRITE);
        public static readonly VCPProperty   VIDEO_GAIN_DRIVE_RED                = new VCPProperty(0x16, "Video Gain (Drive): Red", VCPPropertyPerms.READ_WRITE);
        public static readonly VCPProperty   VIDEO_GAIN_DRIVE_GREEN              = new VCPProperty(0x18, "Video Gain (Drive): Green", VCPPropertyPerms.READ_WRITE);
        public static readonly VCPProperty   VIDEO_GAIN_DRIVE_BLUE               = new VCPProperty(0x1A, "Video Gain (Drive): Blue", VCPPropertyPerms.READ_WRITE);
        public static readonly VCPProperty   ACTIVE_CONTROL                      = new VCPProperty(0x52, "Active Control", VCPPropertyPerms.READ);
        public static readonly VCPProperty   INPUT_SELECT                        = new VCPProperty(0x60, "Input Select", VCPPropertyPerms.READ_WRITE);
        public static readonly VCPProperty   HORIZONTAL_FREQUENCY                = new VCPProperty(0xAC, "Horizontal Frequency", VCPPropertyPerms.READ);
        public static readonly VCPProperty   VERTICAL_FREQUENCY                  = new VCPProperty(0xAE, "Vertical Frequency", VCPPropertyPerms.READ);
        public static readonly VCPProperty   FLAT_PANEL_SUB_PIXEL_LAYOUT         = new VCPProperty(0xB2, "Flat Panel Sub-Pixel Layout", VCPPropertyPerms.READ);
        public static readonly VCPProperty   DISPLAY_TECHNOLOGY_TYPE             = new VCPProperty(0xB6, "Display Technology Type", VCPPropertyPerms.READ);
        public static readonly VCPProperty   DISPLAY_USAGE_TIME                  = new VCPProperty(0xC0, "Display Usage Time", VCPPropertyPerms.READ);
        public static readonly VCPProperty   APPLICATION_ENABLE_KEY              = new VCPProperty(0xC6, "Application Enable Key", VCPPropertyPerms.READ);
        public static readonly VCPProperty   DISPLAY_CONTROLLER_ID               = new VCPProperty(0xC8, "Display Controller ID", VCPPropertyPerms.READ_WRITE);
        public static readonly VCPProperty   DISPLAY_FIRMWARE_LEVEL              = new VCPProperty(0xC9, "Display Firmware Level", VCPPropertyPerms.READ);
        public static readonly VCPProperty   OSD                                 = new VCPProperty(0xCA, "OSD", VCPPropertyPerms.READ_WRITE);
        public static readonly VCPProperty   OSD_LANGUAGE                        = new VCPProperty(0xCC, "OSD Language", VCPPropertyPerms.READ_WRITE);
        public static readonly VCPProperty   POWER_MODE                          = new VCPProperty(0xD6, "Power mode", VCPPropertyPerms.READ_WRITE);


        public static readonly List<VCPProperty> List = new List<VCPProperty>() {
            NEW_CONTROL_VALUE,
            RESTORE_FACTORY_DEFAULTS,
            RESTORE_FACTORY_LUMINANCE_CONTRAST,
            RESTORE_FACTORY_COLOR_DEFAULTS,
            COLOR_TEMPERATURE_INCREMENT,
            COLOR_TEMPERATURE_REQUEST,
            BRIGHTNESS,
            CONTRAST,
            SELECT_COLOR_PRESET,
            VIDEO_GAIN_DRIVE_RED,
            VIDEO_GAIN_DRIVE_GREEN,
            VIDEO_GAIN_DRIVE_BLUE,
            ACTIVE_CONTROL,
            INPUT_SELECT,
            HORIZONTAL_FREQUENCY,
            VERTICAL_FREQUENCY,
            FLAT_PANEL_SUB_PIXEL_LAYOUT,
            DISPLAY_TECHNOLOGY_TYPE,
            DISPLAY_USAGE_TIME,
            APPLICATION_ENABLE_KEY,
            DISPLAY_CONTROLLER_ID,
            DISPLAY_FIRMWARE_LEVEL,
            OSD,
            OSD_LANGUAGE,
            POWER_MODE
        };

        public static Dictionary<uint, VCPProperty> IndexByHexCode {
            get {
                return List.ToDictionary(prop => prop.code, prop => prop);
            } 
        }

        public static  Dictionary<string, VCPProperty> IndexByCodeName { 
            get {
                return List.ToDictionary(prop => prop.codeName, prop => prop);
            }
        }
    }
    
}
