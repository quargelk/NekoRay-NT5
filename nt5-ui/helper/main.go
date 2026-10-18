// nt5helper: helper program of NekoRay NT5.
//
//	nt5helper fetch ...   downloads one URL with TLS 1.2/1.3 and certificate verification
//	nt5helper version
//
// Built with the Go 1.20 toolchain that has the NT 5.1 backports (Windows XP); see build.cmd.
// Messages never contain the downloaded URL: it usually carries an access token.
package main

import (
	"fmt"
	"os"
)

const version = "1.2.0"

func main() {
	if len(os.Args) < 2 {
		usage()
		os.Exit(2)
	}
	switch os.Args[1] {
	case "fetch":
		os.Exit(runFetch(os.Args[2:]))
	case "version":
		fmt.Println("nt5helper " + version)
	default:
		usage()
		os.Exit(2)
	}
}

func usage() {
	fmt.Fprintln(os.Stderr, "usage: nt5helper fetch -url URL -out FILE [-max BYTES] [-timeout SECONDS] [-proxy http://HOST:PORT] [-ca FILE] [-headers FILE] [-meta FILE] [-agent UA]")
	fmt.Fprintln(os.Stderr, "       nt5helper fetch -url-file FILE -out FILE ...   (reads the URL from a file)")
	fmt.Fprintln(os.Stderr, "       nt5helper version")
}
