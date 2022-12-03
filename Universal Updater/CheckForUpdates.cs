using System;
using System.Collections.Generic;
using System.Text;

namespace Universal_Updater
{
    class CheckForUpdates
    {
        public static int Choice { get; private set; }

        public static string AvailableUpdates()
        {
            int major = Convert.ToInt32(GetDeviceInfo.OSVersion[0].Split(',')[2].Split('.')[0]), minor = Convert.ToInt32(GetDeviceInfo.OSVersion[0].Split(',')[2].Split('.')[1]), build = Convert.ToInt32(GetDeviceInfo.OSVersion[0].Split(',')[2].Split('.')[2]), revision = Convert.ToInt32(GetDeviceInfo.OSVersion[0].Split(',')[2].Split('.')[3]);
            string OSVersion = string.Format("{0}.{1}.{2}.{3}", major, minor, build, revision);
            if (CompareUpdates(OSVersion, "8.10.14219.341") == "lss")
            {
                Choice = 1;
                return "No updates available.";
            }
            else if (major == 8)
            {
                Choice = 2;
                return "1. 10.0.10549.4\n2. 10.0.10586.107\n3. Offline update packages";
            }
            else if (CompareUpdates(OSVersion, "10.0.10570.0") == "lss")
            {
                Choice = 3;
                return "1. 10.0.10570.0\n2. Offline update packages";
            }
            else if (CompareUpdates(OSVersion, "10.0.14393.1066") == "lss")
            {
                Choice = 4;
                return "1. 10.0.14393.1066\n2. Offline update packages";
            }
            else if (CompareUpdates(OSVersion, "10.0.15063.297") == "lss")
            {
                Choice = 5;
                return "1. 10.0.15063.297\n2. 10.0.15254.603\n3. Offline update packages";
            }
            else if (CompareUpdates(OSVersion, "10.0.15254.603") == "lss")
            {
                Choice = 6;
                return "1. 10.0.15254.603\n2. Offline update packages";
            }
            else if (CompareUpdates(OSVersion, "10.0.15254.603") == "gtr" || CompareUpdates(OSVersion, "10.0.15254.603") == "equ")
            {
                Choice = 7;
                return "1. Offline update packages";
            }
            else
            {
                Choice = 0;
                return "Something went wrong.";
            }
        }

        public static string CompareUpdates(string current, string target)
        {
            var result = string.Empty;
            var targetNumberArray = target.Trim().Split('.');
            var currentNumberArray = current.Trim().Split('.');
            if (int.Parse(targetNumberArray[0]) < int.Parse(currentNumberArray[0]))
            {
                result = "gtr";
            }
            else if (int.Parse(targetNumberArray[0]) == int.Parse(currentNumberArray[0]))
            {
                if (int.Parse(targetNumberArray[1]) < int.Parse(currentNumberArray[1]))
                {
                    result = "gtr";
                }
                else if (int.Parse(targetNumberArray[1]) == int.Parse(currentNumberArray[1]))
                {
                    if (int.Parse(targetNumberArray[2]) < int.Parse(currentNumberArray[2]))
                    {
                        result = "gtr";
                    }
                    else if(int.Parse(targetNumberArray[2]) == int.Parse(currentNumberArray[2]))
                    {
                        if (int.Parse(targetNumberArray[3]) < int.Parse(currentNumberArray[3]))
                        {
                            result = "gtr";
                        }
                        else if (int.Parse(targetNumberArray[3]) == int.Parse(currentNumberArray[3]))
                        {
                            result = "equ";
                        }
                        else
                        {
                            result = "lss";
                        }
                    }
                    else
                    {
                        result = "lss";
                    }
                }
                else
                {
                    result = "lss";
                }
            }
            else
            {
                result = "lss";
            }
            return result;
        }
    }
}
