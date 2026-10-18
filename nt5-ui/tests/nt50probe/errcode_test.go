package main

import (
	"errors"
	"net"
	"os"
	"syscall"
	"testing"
)

// The decision must come from the code, whatever text Windows gives the error.
func TestIsConnRefused(t *testing.T) {
	wrap := func(call string, e error) error {
		return &net.OpError{Op: "dial", Net: "tcp", Err: os.NewSyscallError(call, e)}
	}
	if !isConnRefused(wrap("connectex", syscall.Errno(10061))) {
		t.Error("connectex 10061 not recognised")
	}
	if !isConnRefused(wrap("connect", syscall.Errno(10061))) { // Windows 2000 path (no ConnectEx)
		t.Error("connect 10061 not recognised")
	}
	if isConnRefused(wrap("connectex", syscall.Errno(10060))) { // WSAETIMEDOUT
		t.Error("timeout taken for refused")
	}
	if isConnRefused(errors.New("connection refused")) { // text alone is not enough
		t.Error("plain text taken for refused")
	}
	if isConnRefused(&net.OpError{Op: "read", Err: os.NewSyscallError("wsarecv", syscall.Errno(10061))}) {
		t.Error("non-dial error taken for refused")
	}
}

// A real dial to a closed loopback port (in the local system language).
func TestRealRefused(t *testing.T) {
	l, err := net.Listen("tcp", "127.0.0.1:0")
	if err != nil {
		t.Fatal(err)
	}
	addr := l.Addr().String()
	l.Close()
	_, err = net.Dial("tcp", addr)
	if !isConnRefused(err) {
		t.Fatalf("%v [%s]", err, errnoChain(err))
	}
}
