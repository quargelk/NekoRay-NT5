// peinfo prints the PE header fields and the static import table that decide whether the
// Windows NT 5.0 loader accepts an executable. Read-only.
package main

import (
	"debug/pe"
	"fmt"
	"os"
	"sort"
	"strings"
)

func main() {
	for _, path := range os.Args[1:] {
		f, err := pe.Open(path)
		if err != nil {
			fmt.Println(path, "error:", err)
			continue
		}
		fmt.Println(path)
		if oh, ok := f.OptionalHeader.(*pe.OptionalHeader32); ok {
			fmt.Printf("  machine=0x%x subsystem=%d subsys-version=%d.%d os-version=%d.%d stack=0x%x/0x%x checksum=0x%x\n",
				f.Machine, oh.Subsystem, oh.MajorSubsystemVersion, oh.MinorSubsystemVersion,
				oh.MajorOperatingSystemVersion, oh.MinorOperatingSystemVersion,
				oh.SizeOfStackReserve, oh.SizeOfStackCommit, oh.CheckSum)
		}
		for _, s := range f.Sections {
			fmt.Printf("  section %-8s vsize=0x%x size=0x%x\n", s.Name, s.VirtualSize, s.Size)
		}
		syms, err := f.ImportedSymbols()
		if err != nil {
			fmt.Println("  imports error:", err)
		}
		byDll := map[string][]string{}
		for _, s := range syms {
			i := strings.LastIndex(s, ":")
			byDll[strings.ToLower(s[i+1:])] = append(byDll[strings.ToLower(s[i+1:])], s[:i])
		}
		var dlls []string
		for d := range byDll {
			dlls = append(dlls, d)
		}
		sort.Strings(dlls)
		for _, d := range dlls {
			sort.Strings(byDll[d])
			fmt.Printf("  %s (%d): %s\n", d, len(byDll[d]), strings.Join(byDll[d], " "))
		}
		f.Close()
	}
}
