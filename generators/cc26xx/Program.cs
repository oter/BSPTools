using BSPEngine;
/* Copyright (c) 2016 Sysprogs OU. All Rights Reserved.
   This software is licensed under the Sysprogs BSP Generator License.
   https://github.com/sysprogs/BSPTools/blob/master/LICENSE
*/

using BSPGenerationTools;
using LinkerScriptGenerator;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Text;
using System.Threading.Tasks;

namespace cc26xx_bsp_generator
{
    class Program
    {
        static void Main(string[] args)
        {
            if (args.Length < 1)
            {
                throw new Exception("Usage: cc2600_bsp_generator.exe <cc26xx SW package directory>");
            }

            var bspBuilder = new Cc26xxBuilder(new BSPDirectories(args[0], @"..\..\Output", @"..\..\rules"));

            var devices = BSPGeneratorTools.ReadMCUDevicesFromCommaDelimitedCSVFile(
                bspBuilder.Directories.RulesDir + @"\cc26xxdevices.csv",
                "Part Number",
                "Non-volatile Memory (KB)",
                "RAM(KB)",
                "CPU",
                true);

            var allMCUFamilyBuilders = new List<MCUFamilyBuilder>();
            foreach (var fn in Directory.GetFiles(bspBuilder.Directories.RulesDir + @"\Families", "*.xml"))
            {
                allMCUFamilyBuilders.Add(new MCUFamilyBuilder(bspBuilder, XmlTools.LoadObject<FamilyDefinition>(fn)));
            }

            var rejects = BSPGeneratorTools.AssignMCUsToFamilies(devices, allMCUFamilyBuilders);

            var commonPseudofamily = new MCUFamilyBuilder(bspBuilder, XmlTools.LoadObject<FamilyDefinition>(bspBuilder.Directories.RulesDir + @"\CommonFiles.xml"));
            var flags = new ToolFlags();
            var projectFiles = new List<string>();
            commonPseudofamily.CopyFamilyFiles(ref flags, projectFiles);

            var exampleDirs = new List<string>();
            foreach (var sample in commonPseudofamily.CopySamples())
            {
                exampleDirs.Add(sample);
            }
            
            bool noPeripheralRegisters = args.Contains("/noperiph");
            var familyDefinitions = new List<MCUFamily>();
            var mcuDefinitions = new List<MCU>();
            var frameworks = new List<EmbeddedFramework>();

            foreach (var mcuFamilyBuilder in allMCUFamilyBuilders)
            {
                var rejectedMCUs = mcuFamilyBuilder.RemoveUnsupportedMCUs(true);
                if (rejectedMCUs.Length != 0)
                {
                    Console.WriteLine("Unsupported {0} MCUs:", mcuFamilyBuilder.Definition.Name);
                    foreach (var mcu in rejectedMCUs)
                    {
                        Console.WriteLine("\t{0}", mcu.Name);
                    }
                }

                mcuFamilyBuilder.AttachStartupFiles(new[]
                {
                    StartupFilesParser.Parse(
                        mcuFamilyBuilder.Definition.Name,
                        mcuFamilyBuilder.Definition.PrimaryHeaderDir,
                        "startup_gcc.c",
                        bspBuilder.SystemVars)
                 });

                HardwareRegisterSet[] regs = RegistersParserXml.GenerateFamilyPeripheralRegisters(@"..\..\registers", Path.Combine(mcuFamilyBuilder.Definition.PrimaryHeaderDir, @"inc\hw_memmap.h"));

                mcuFamilyBuilder.AttachPeripheralRegisters(new[]
                {
                    new MCUDefinitionWithPredicate
                    {
                        MCUName = mcuFamilyBuilder.Definition.Name,
                        RegisterSets = regs.ToArray(),
                        MatchPredicate = m => true
                    }
                });


                var familyObject = mcuFamilyBuilder.GenerateFamilyObject(true);

                familyObject.AdditionalSourceFiles = LoadedBSP.Combine(familyObject.AdditionalSourceFiles, projectFiles.Where(f => !MCUFamilyBuilder.IsHeaderFile(f)).ToArray());
                familyObject.AdditionalHeaderFiles = LoadedBSP.Combine(familyObject.AdditionalHeaderFiles, projectFiles.Where(f => MCUFamilyBuilder.IsHeaderFile(f)).ToArray());

                familyObject.AdditionalSystemVars = LoadedBSP.Combine(familyObject.AdditionalSystemVars, commonPseudofamily.Definition.AdditionalSystemVars);
                familyObject.CompilationFlags = familyObject.CompilationFlags.Merge(flags);

                familyDefinitions.Add(familyObject);
                mcuFamilyBuilder.GenerateLinkerScripts(false);

                foreach (var mcu in mcuFamilyBuilder.MCUs)
                {
                    mcuDefinitions.Add(mcu.GenerateDefinition(mcuFamilyBuilder, bspBuilder, !noPeripheralRegisters));
                }

                foreach (var fw in mcuFamilyBuilder.GenerateFrameworkDefinitions())
                {
                    frameworks.Add(fw);
                }

                foreach (var sample in mcuFamilyBuilder.CopySamples())
                {
                    exampleDirs.Add(sample);
                }
            }

            BoardSupportPackage bsp = new BoardSupportPackage
            {
                PackageID = "com.sysprogs.arm.ti.cc26xx",
                PackageDescription = "TI CC26XX Devices",
                GNUTargetID = "arm-eabi",
                GeneratedMakFileName = "cc26xx.mak",
                MCUFamilies = familyDefinitions.ToArray(),
                SupportedMCUs = mcuDefinitions.ToArray(),
                Frameworks = frameworks.ToArray(),
                Examples = exampleDirs.ToArray(),
                PackageVersion = "1.0"
            };

            bspBuilder.Save(bsp, true);
        }

        class Cc26xxBuilder : BSPBuilder
        {
            const uint FLASH_Base       = 0x00000000; 
            const uint FLASH_Size       = 0x0001FFA8;
            const uint FLASH_CCFG_Base  = 0x0001FFA8;
            const uint FLASH_CCFG_Size  = 0x00000058;
            const uint SRAM_Base        = 0x20000000;
            const uint SRAM_Size        = 0x00005000;
            const uint GPRAM_Base       = 0x11000000;
            const uint GPRAM_Size       = 0x00002000;

            public Cc26xxBuilder(BSPDirectories dirs)
                : base(dirs, @"..\..\rules\cc26xx.ldsx")
            {
                ShortName = "cc26xx";
            }

            public override void GetMemoryBases(out uint flashBase, out uint ramBase)
            {
                flashBase = FLASH_Base;
                ramBase = SRAM_Base;
            }

            public override MemoryLayout GetMemoryLayout(MCUBuilder mcu, MCUFamilyBuilder family)
            {
                var layout = new MemoryLayout { DeviceName = mcu.Name, Memories = new List<Memory>() };

                layout.Memories.Add(new Memory
                {
                    Name = "FLASH",
                    Access = MemoryAccess.Readable | MemoryAccess.Executable,
                    Type = MemoryType.FLASH,
                    Start = FLASH_Base,
                    Size = FLASH_Size
                });

                layout.Memories.Add(new Memory
                {
                    Name = "FLASH_CCFG",
                    Access = MemoryAccess.Readable | MemoryAccess.Executable,
                    Type = MemoryType.FLASH,
                    Start = FLASH_CCFG_Base,
                    Size = FLASH_CCFG_Size
                });

                layout.Memories.Add(new Memory
                {
                    Name = "SRAM",
                    Access = MemoryAccess.Readable | MemoryAccess.Writable | MemoryAccess.Executable,
                    Type = MemoryType.RAM,
                    Start = SRAM_Base,
                    Size = SRAM_Size
                });

                layout.Memories.Add(new Memory
                {
                    Name = "GPRAM",
                    Access = MemoryAccess.Readable | MemoryAccess.Writable | MemoryAccess.Executable,
                    Type = MemoryType.RAM,
                    Start = GPRAM_Base,
                    Size = GPRAM_Size
                });

                return layout;
            }
        }
    }
}
