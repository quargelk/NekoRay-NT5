//go:build !nt50

package main

func exceptionPaths() string { return "not counted (not an nt50 build)" }

func simulated() string { return "not an nt50 build" }
