/* Copyright (C) 2021 - Mywk.Net
 * Licensed under the EUPL, Version 1.2
 * You may obtain a copy of the Licence at: https://joinup.ec.europa.eu/community/eupl/og_page/eupl
 * Unless required by applicable law or agreed to in writing, software distributed under the Licence is distributed on an "AS IS" basis, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 */
using System;
using System.Buffers.Text;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Controls;

namespace Process_Affinity_Utility
{
    public class Profile
    {
        public string ProcessName { get; set; }
        public Int64 ProcessAffinity { get; set; }

        /// <summary>
        /// Creates a profile
        /// </summary>
        /// <param name="processName"></param>
        /// <param name="processAffinity"></param>
        public Profile(string processName, Int64 processAffinity)
        {
            ProcessName = processName;
            ProcessAffinity = processAffinity;
        }

        /// <summary>
        /// Creates a profile based on a saved string
        /// </summary>
        /// <param name="base64EncodedData">base64(process-processAffinity)</param>
        public Profile(string base64EncodedData)
        {
            try
            {
                var base64EncodedBytes = System.Convert.FromBase64String(base64EncodedData);
                var dataString = System.Text.Encoding.UTF8.GetString(base64EncodedBytes);

                int separatorIndex = dataString.LastIndexOf('-');
                string processName = dataString.Substring(0, separatorIndex);

                string processAffinityString =
                    dataString.Substring(separatorIndex + 1, dataString.Length - separatorIndex - 1);
                Int64 processAffinity = Int64.Parse(processAffinityString);

                ProcessName = processName;
                ProcessAffinity = processAffinity;
            }
            catch (Exception)
            {
                throw new Exception("Invalid profile.");
            }
        }

        /// <summary>
        /// Convert Profile information into something we can quickly and easily save
        /// </summary>
        /// <returns></returns>
        public string ToBase64String()
        {
            var dataBytes = System.Text.Encoding.UTF8.GetBytes(ProcessName + "-" + ProcessAffinity.ToString());
            return System.Convert.ToBase64String(dataBytes);
        }

        /// <summary>
        /// Specially formatted string for usage in a ListBox
        /// </summary>
        /// <returns></returns>
        public override string ToString()
        {
            var coresString = "";

            // Convert the bitmask to bits without using the BitArray class
            var bitMaskString = Convert.ToString(ProcessAffinity, 2);

            bool[] bits = bitMaskString.PadLeft(8, '0').Select(c => (c == '1')).ToArray();
            Array.Reverse(bits);

            // Go through the values and set them on the processor combobox accordingly
            for (int i = 0; i < bits.Length; i++)
            {
                if(bits[i])
                {
                    if (i != 0)
                        coresString += ",";

                    coresString += i;
                }
            }

            if (coresString.Length > 0 && coresString[0] == ',')
                coresString = coresString.Remove(0, 1);

            return ProcessName + " [" + coresString + "]";
        }
    }
}
