package main

import (
	"crypto/sha256"
	"fmt"
	"io"
	"os"
)

// hash FILE...: SHA-256 of each file, so that the log of a target machine names the exact binaries
// it tested (Windows 2000 and XP have no built-in SHA-256 tool).
func runHash(files []string) {
	for _, name := range files {
		f, err := os.Open(name)
		if err != nil {
			fail("sha256", "%s: %v", name, err)
			continue
		}
		h := sha256.New()
		_, err = io.Copy(h, f)
		f.Close()
		if err != nil {
			fail("sha256", "%s: %v", name, err)
			continue
		}
		fmt.Printf("SHA256 %x  %s\n", h.Sum(nil), name)
	}
}
