package main

import (
	"errors"
	"fmt"
	"net"
	"syscall"
)

// WSAECONNREFUSED: the connection was refused (no listener on the port).
const wsaeconnrefused = syscall.Errno(10061)

// isConnRefused reports whether err is a failed dial whose cause is WSAECONNREFUSED. It unwraps
// *net.OpError -> *os.SyscallError -> syscall.Errno with errors.Is/As (Go 1.13+), so it does not
// depend on the language of the Windows error message.
func isConnRefused(err error) bool {
	var op *net.OpError
	if !errors.As(err, &op) || op.Op != "dial" {
		return false
	}
	return errors.Is(err, wsaeconnrefused)
}

// errnoChain names the operation and the Winsock code inside err, for the report.
func errnoChain(err error) string {
	s := "no errno"
	var en syscall.Errno
	if errors.As(err, &en) {
		s = fmt.Sprintf("errno %d", uint32(en))
	}
	var op *net.OpError
	if errors.As(err, &op) {
		s = "op " + op.Op + ", " + s
	}
	return s
}
