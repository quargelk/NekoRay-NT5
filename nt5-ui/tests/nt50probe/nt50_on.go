//go:build nt50

// Built with -tags nt50 by the candidate (NT 5.0) toolchain only: reads the runtime's counters of
// the exception paths, and whether this is a test build that simulates Windows 2000 on a newer system.
package main

import (
	"fmt"
	_ "unsafe"
)

//go:linkname nt50SEHCalls runtime.nt50SEHCalls
var nt50SEHCalls uint32

//go:linkname nt50UEFCalls runtime.nt50UEFCalls
var nt50UEFCalls uint32

//go:linkname runtimeSimulate runtime.nt50Simulate
var runtimeSimulate string

//go:linkname stdSimulate internal/syscall/windows.nt50Simulate
var stdSimulate string

func exceptionPaths() string {
	return fmt.Sprintf("handled by SEH frame %d, by top-level filter %d", nt50SEHCalls, nt50UEFCalls)
}

func simulated() string {
	return fmt.Sprintf("runtime=%q std=%q", runtimeSimulate, stdSimulate)
}
