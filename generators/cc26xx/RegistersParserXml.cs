using BSPEngine;
using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using System.Linq;
using System.Text.RegularExpressions;

namespace cc26xx_bsp_generator
{
    static class RegistersParserXml
    {
        private static Dictionary<string, List<string>> DUPLICATES = new Dictionary<string, List<string>>
        {
            { "AUX_AIODIO", new List<string> { "AUX_AIODIO0", "AUX_AIODIO1" } },
            { "GPT", new List<string> { "GPT0", "GPT1", "GPT2", "GPT3" } },
            { "I2C", new List<string> { "I2C0" } },
            { "I2S", new List<string> { "I2S0" } },
            { "SSI", new List<string> { "SSI0", "SSI1" } },
            { "UART", new List<string> { "UART0" } },
            { "UDMA", new List<string> { "UDMA0" } },
        };

        

        private static Dictionary<string, KeyValuePair<string, ulong>> ProcessRegisterSetAddresses(string file)
        {
            Dictionary<string, KeyValuePair<string, ulong>> addresses = new Dictionary<string, KeyValuePair<string, ulong>>();
            Regex basedef_regex = new Regex(@"^#define[ \t]+((GPIO_PORT)[A-Z]|(GPIO_PORT)[A-Z](_AHB)|SHAMD5|([A-Z0-9_]+?)[0-9]?)_BASE[ \t]+0x([0-9xa-fA-F]{8})[ \t]+\/\/ (.*?)[\r]?$", RegexOptions.Multiline);

            foreach (Match m in basedef_regex.Matches(File.ReadAllText(file)))
            {
                string name = m.Groups[1].ToString();
                string type = m.Groups[2].ToString();
                if (type == "")
                    type = m.Groups[3].ToString() + m.Groups[4].ToString();
                if (type == "")
                    type = m.Groups[5].ToString();
                if (type == "")
                    type = name;
                ulong address = ulong.Parse(m.Groups[6].ToString(), System.Globalization.NumberStyles.HexNumber);
                string comment = m.Groups[7].ToString();

                addresses.Add(name, new KeyValuePair<string, ulong>(type, address));
            }

            return addresses;
        }

        private static int GetBitOffset(int mask)
        {
            int offset = 0;
            if (mask == 0)
            {
                throw new Exception("Mask cannot be empty.");
            }
            while((mask & 1) == 0)
            {
                ++offset;
                mask >>= 1;
            }
            return offset;
        }

        public static HardwareRegisterSet[] GenerateFamilyPeripheralRegisters(string registersXmlsPath, string memMapPath)
        {
            var registerAddrs = ProcessRegisterSetAddresses(memMapPath);
            var xmlFiles = Directory.GetFiles(registersXmlsPath, "*.xml");

            var registersSet = new List<HardwareRegisterSet> { };
            foreach (var xmlFile in xmlFiles)
            {
                XmlDocument doc = new XmlDocument();
                doc.Load(xmlFile);

                var module = doc.DocumentElement;
                if (module.Name != "module")
                {
                    throw new Exception("There is no module node in the xml.");
                }
                var moduleDescription = module.Attributes["description"];
                if (moduleDescription == null)
                {
                    throw new Exception("No description for the module. Maybe the xml file is incorrect.");
                }

                var registers = new List<HardwareRegister> { };
                var registersSetName = module.Attributes["id"].Value;

                var regs = new List<string> { };
                if (DUPLICATES.Keys.Contains(registersSetName))
                {
                    regs = DUPLICATES[registersSetName];
                }
                else
                {
                    regs.Add(registersSetName);
                }

                foreach (var setName in regs)
                {
                    int baseAddress = (int)(registerAddrs[setName].Value);

                    // foreach register
                    foreach (XmlNode reg in doc.DocumentElement.ChildNodes)
                    {
                        var readOnly = false;
                        int offset = int.Parse(reg.Attributes["offset"].Value.Substring(2), System.Globalization.NumberStyles.HexNumber);
                        var id = reg.Attributes["id"].Value;
                        int width = int.Parse(reg.Attributes["width"].Value, System.Globalization.NumberStyles.Integer);

                        var subRegisters = new List<HardwareSubRegister> { };
                        // foreach bitfield
                        foreach (XmlNode bitField in reg.ChildNodes)
                        {
                            readOnly = readOnly || (bitField.Attributes["rwaccess"].Value.ToLower() == "ro");
                            int end = int.Parse(bitField.Attributes["end"].Value, System.Globalization.NumberStyles.Integer);
                            var bitFieldName = bitField.Attributes["id"].Value;

                            // Check for valid bits
                            var validBits = true;
                            foreach (XmlNode bitEnum in bitField.ChildNodes)
                            {
                                int bitMask = int.Parse(bitEnum.Attributes["value"].Value, System.Globalization.NumberStyles.Integer);
                                int bitOffset = bitMask << end;
                                var subregisterName = bitEnum.Attributes["id"].Value;
                                if ((bitOffset & (bitOffset - 1)) != 0)
                                {
                                    validBits = false;
                                    System.Console.WriteLine("Ignore sub-register " + subregisterName + " due to its invalid bit offset.");
                                }
                            }
                            if (!validBits)
                            {
                                System.Console.WriteLine("Ignore bit field " + bitFieldName);
                                continue;
                            }

                                // foreach bitenum
                            foreach (XmlNode bitEnum in bitField.ChildNodes)
                            {
                                int bitMask = int.Parse(bitEnum.Attributes["value"].Value, System.Globalization.NumberStyles.Integer);
                                if (bitMask == 0)
                                {
                                    continue;
                                }
                                subRegisters.Add(new HardwareSubRegister
                                {
                                    FirstBit = GetBitOffset(bitMask << end),
                                    KnownValues = new KnownSubRegisterValue[] { new KnownSubRegisterValue { Name = bitFieldName } },
                                    Name = bitEnum.Attributes["id"].Value,
                                    ParentRegister = null,
                                    SizeInBits = 1
                                });
                                
                            }
                        }

                        registers.Add(new HardwareRegister
                        {
                            Address = (baseAddress + offset).ToString(),
                            GDBExpression = null,
                            Name = id,
                            ReadOnly = readOnly,
                            SizeInBits = width,
                            SubRegisters = subRegisters.ToArray()
                        });

                    }

                    registersSet.Add(new HardwareRegisterSet
                    {
                        ExpressionPrefix = setName,
                        UserFriendlyName = setName,
                        Registers = registers.ToArray()
                    });
                }
            }

                
            

            return registersSet.ToArray();
        }
    }
}
