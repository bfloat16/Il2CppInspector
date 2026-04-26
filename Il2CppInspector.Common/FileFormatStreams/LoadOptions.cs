/*
    Copyright 2017-2021 Katy Coe - http://www.djkaty.com - https://github.com/djkaty

    All rights reserved.
*/

namespace Il2CppInspector
{
    // Modifiers for use when loading binary files
    public class LoadOptions
    {
        // For ELF files, the virtual address to which we should rebase - ignored for other file types
        // Use zero to prevent rebasing
        public ulong ImageBase { get; set; } = 0ul;
    }
}
