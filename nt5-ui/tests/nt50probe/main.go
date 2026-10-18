// nt50probe exercises the parts of the Go runtime and standard library that Xray and nt5helper
// depend on, so that Windows 2000 and Windows XP can be checked before Xray itself. It prints one
// line per check (PASS/FAIL/INFO) and exits with the number of failed checks (2 = crash).
//
//	nt50probe                     all checks that need no network
//	nt50probe crash-go            unrecovered nil dereference: Go panic report, exit code 2
//	nt50probe crash-native        access violation in Windows code: runtime crash report, exit code 2
//	nt50probe dns NAME            resolve NAME through the system (needs DNS)
//	nt50probe https URL CAFILE    HTTPS GET with certificate verification against CAFILE (needs network)
//	nt50probe e2e DIR             local VLESS+REALITY+Vision test with xray.exe from DIR (see e2e.go)
package main

import (
	"bufio"
	"context"
	"crypto/ecdsa"
	"crypto/elliptic"
	"crypto/rand"
	"crypto/rsa"
	"crypto/sha256"
	"crypto/tls"
	"crypto/x509"
	"crypto/x509/pkix"
	"errors"
	"fmt"
	"io"
	"math/big"
	"net"
	"net/http"
	"os"
	"os/exec"
	"path/filepath"
	"runtime"
	"runtime/debug"
	"strings"
	"sync"
	"sync/atomic"
	"syscall"
	"time"
	"unsafe"
)

const version = "nt50probe 2"

var failed int

func pass(name, format string, a ...interface{}) {
	fmt.Printf("PASS %-14s %s\n", name, fmt.Sprintf(format, a...))
}

func fail(name, format string, a ...interface{}) {
	failed++
	fmt.Printf("FAIL %-14s %s\n", name, fmt.Sprintf(format, a...))
}

func info(name, format string, a ...interface{}) {
	fmt.Printf("INFO %-14s %s\n", name, fmt.Sprintf(format, a...))
}

// check runs fn and turns a panic into a failure, so that one broken check does not hide the others.
func check(name string, fn func() error) {
	start := time.Now()
	err := func() (err error) {
		defer func() {
			if r := recover(); r != nil {
				err = fmt.Errorf("panic: %v\n%s", r, debug.Stack())
			}
		}()
		return fn()
	}()
	if err != nil {
		fail(name, "%v", err)
		return
	}
	pass(name, "%v", time.Since(start).Round(time.Millisecond))
}

func main() {
	args := os.Args[1:]
	cmd := ""
	if len(args) > 0 {
		cmd = args[0]
	}
	switch cmd {
	case "":
		runAll()
	case "crash-go":
		crashGo()
	case "crash-native":
		crashNative()
	case "hash":
		runHash(args[1:])
	case "e2e":
		if len(args) < 2 {
			usage()
		}
		xray := ""
		if len(args) > 2 {
			xray = args[2]
		}
		runE2E(args[1], xray)
	case "child":
		fmt.Println("child ok")
		os.Exit(7)
	case "dns":
		if len(args) < 2 {
			usage()
		}
		header()
		check("dns-remote", func() error { return checkDNSName(args[1]) })
	case "https":
		if len(args) < 3 {
			usage()
		}
		header()
		check("https-remote", func() error { return checkHTTPS(args[1], args[2]) })
	default:
		usage()
	}
	fmt.Printf("RESULT %d failed\n", failed)
	os.Exit(failed)
}

func usage() {
	fmt.Println(version)
	fmt.Println("usage: nt50probe [crash-go | crash-native | dns NAME | https URL CAFILE | e2e DIR [XRAY] | hash FILE...]")
	os.Exit(100)
}

func runAll() {
	header()
	check("start", func() error { return nil })
	check("goroutines", checkGoroutines)
	check("thread-exit", checkThreadExit)
	check("gc", checkGC)
	check("panic", checkPanic)
	check("nil-deref", checkNilDeref)
	check("nil-deref-deep", checkNilDerefDeep)
	check("div-zero", checkDivZero)
	info("exceptions", "%s", exceptionPaths())
	check("time", checkTime)
	check("rand", checkRand)
	check("file", checkFile)
	check("tcp-loopback", checkTCP)
	check("tcp-parallel", checkTCPParallel)
	check("tcp-refused", checkTCPRefused)
	check("tcp-cancel", checkTCPCancel)
	check("read-deadline", checkReadDeadline)
	check("udp-loopback", checkUDP)
	check("dns-local", checkDNSLocal)
	check("port-lookup", checkPortLookup)
	check("interfaces", checkInterfaces)
	check("tls-verify", checkTLS)
	check("exec", checkExec)
	check("after-all", checkGoroutines)
	info("exceptions", "%s", exceptionPaths())
}

// ---- system information

func header() {
	fmt.Println(version, runtime.Version(), runtime.GOOS+"/"+runtime.GOARCH, "NumCPU", runtime.NumCPU())
	v, _ := syscall.GetVersion()
	info("windows", "%d.%d build %d", byte(v), uint8(v>>8), v>>16)
	k32 := syscall.NewLazyDLL("kernel32.dll")
	info("veh", "AddVectoredExceptionHandler %s (absent: SEH frames are used)", present(k32.NewProc("AddVectoredExceptionHandler")))
	info("rtlgenrandom", "advapi32 SystemFunction036 %s (absent: CryptGenRandom)", present(syscall.NewLazyDLL("advapi32.dll").NewProc("SystemFunction036")))
	info("connectex", "ConnectEx %s (absent: non-blocking connect + select)", errPresent(syscall.LoadConnectEx()))
	info("getaddrinfow", "GetAddrInfoW %s (absent: gethostbyname)", errPresent(syscall.LoadGetAddrInfo()))
	info("adapters", "GetAdaptersAddresses %s", present(syscall.NewLazyDLL("iphlpapi.dll").NewProc("GetAdaptersAddresses")))
	info("cancelioex", "CancelIoEx %s", errPresent(syscall.LoadCancelIoEx()))
	info("simulate", "%s", simulated())
}

func present(p *syscall.LazyProc) string {
	return errPresent(p.Find())
}

func errPresent(err error) string {
	if err != nil {
		return "absent"
	}
	return "present"
}

// ---- runtime

func checkGoroutines() error {
	const n = 2000
	var wg sync.WaitGroup
	var sum int64
	ch := make(chan int, 64)
	for i := 0; i < n; i++ {
		wg.Add(1)
		go func(i int) {
			defer wg.Done()
			ch <- i
			atomic.AddInt64(&sum, int64(<-ch))
			runtime.Gosched()
		}(i)
	}
	wg.Wait()
	if sum != n*(n-1)/2 {
		return fmt.Errorf("sum %d, want %d", sum, n*(n-1)/2)
	}
	// work on several threads at once (preemption of busy loops included)
	var spins int64
	deadline := time.Now().Add(200 * time.Millisecond)
	for i := 0; i < 8; i++ {
		wg.Add(1)
		go func() {
			defer wg.Done()
			for time.Now().Before(deadline) {
				atomic.AddInt64(&spins, 1)
			}
		}()
	}
	wg.Wait()
	if spins == 0 {
		return errors.New("no work done")
	}
	return nil
}

// Goroutines that end while locked to their thread make the runtime end that thread, which leaves
// it through tstart_stdcall (the SEH record of the thread is removed there).
func checkThreadExit() error {
	for round := 0; round < 3; round++ {
		var wg sync.WaitGroup
		for i := 0; i < 16; i++ {
			wg.Add(1)
			go func() {
				defer wg.Done()
				runtime.LockOSThread()
				time.Sleep(time.Millisecond)
				// no UnlockOSThread: the thread ends with the goroutine
			}()
		}
		wg.Wait()
	}
	time.Sleep(50 * time.Millisecond)
	// threads created after that must still handle exceptions
	return checkNilDeref()
}

var sink [][]byte

func checkGC() error {
	var before, after runtime.MemStats
	runtime.ReadMemStats(&before)
	var finalized int32
	for i := 0; i < 100; i++ {
		v := new([64]byte)
		runtime.SetFinalizer(v, func(*[64]byte) { atomic.AddInt32(&finalized, 1) })
	}
	for i := 0; i < 400; i++ {
		b := make([]byte, 512<<10)
		b[len(b)-1] = byte(i)
		sink = append(sink, b)
		if len(sink) > 16 {
			sink = sink[1:]
		}
	}
	sink = nil
	runtime.GC()
	runtime.GC()
	time.Sleep(50 * time.Millisecond)
	runtime.ReadMemStats(&after)
	if after.NumGC <= before.NumGC {
		return fmt.Errorf("NumGC %d -> %d", before.NumGC, after.NumGC)
	}
	if atomic.LoadInt32(&finalized) == 0 {
		return errors.New("no finalizer ran")
	}
	debug.FreeOSMemory()
	fmt.Printf("     %-14s allocated %d MB in total, %d collections, heap now %d MB, %d finalizers\n", "",
		(after.TotalAlloc-before.TotalAlloc)>>20, after.NumGC-before.NumGC, after.HeapAlloc>>20, finalized)
	return nil
}

func checkPanic() error {
	got := func() (r interface{}) {
		defer func() { r = recover() }()
		panic("probe")
	}()
	if got != "probe" {
		return fmt.Errorf("recovered %v", got)
	}
	return nil
}

type node struct {
	next *node
	val  int
}

//go:noinline
func deref(p *node) int {
	return p.val
}

// recoverFault runs fn and returns the runtime error that a hardware exception in it became.
func recoverFault(fn func()) (err error) {
	defer func() {
		r := recover()
		if r == nil {
			err = errors.New("no panic")
			return
		}
		re, ok := r.(runtime.Error)
		if !ok {
			err = fmt.Errorf("panic %v is not a runtime.Error", r)
			return
		}
		err = re
	}()
	fn()
	return nil
}

func checkNilDeref() error {
	err := recoverFault(func() { deref(nil) })
	if err == nil || !strings.Contains(err.Error(), "nil pointer dereference") {
		return fmt.Errorf("main goroutine: %v", err)
	}
	errs := make(chan error, 32)
	for i := 0; i < 32; i++ {
		go func() {
			errs <- recoverFault(func() { deref(nil) })
		}()
	}
	for i := 0; i < 32; i++ {
		if err := <-errs; err == nil || !strings.Contains(err.Error(), "nil pointer dereference") {
			return fmt.Errorf("goroutine: %v", err)
		}
	}
	// on a thread of its own
	done := make(chan error)
	go func() {
		runtime.LockOSThread()
		defer runtime.UnlockOSThread()
		done <- recoverFault(func() { deref(nil) })
	}()
	if err := <-done; err == nil || !strings.Contains(err.Error(), "nil pointer dereference") {
		return fmt.Errorf("locked thread: %v", err)
	}
	return nil
}

//go:noinline
func recurse(depth int, pad [8]int) int {
	if depth == 0 {
		return deref(nil) + pad[0]
	}
	return recurse(depth-1, pad) + 1
}

// Faults at many stack depths in fresh goroutines, so that the exception arrives with every amount
// of free space below the stack pointer (the exception is dispatched on the goroutine's stack).
func checkNilDerefDeep() error {
	for depth := 0; depth < 400; depth++ {
		d := depth
		errc := make(chan error)
		go func() { errc <- recoverFault(func() { recurse(d, [8]int{}) }) }()
		if err := <-errc; err == nil || !strings.Contains(err.Error(), "nil pointer dereference") {
			return fmt.Errorf("depth %d: %v", depth, err)
		}
	}
	return nil
}

//go:noinline
func div(a, b int) int {
	return a / b
}

func checkDivZero() error {
	err := recoverFault(func() { div(1, 0) })
	if err == nil || !strings.Contains(err.Error(), "divide by zero") {
		return fmt.Errorf("%v", err)
	}
	return nil
}

func crashGo() {
	header()
	fmt.Println("expect: \"panic: runtime error: invalid memory address or nil pointer dereference\", a goroutine trace, exit code 2")
	os.Stdout.Sync()
	deref(nil)
	fmt.Println("FAIL crash-go: still running")
	os.Exit(1)
}

func crashNative() {
	header()
	fmt.Println("expect: \"Exception 0xc0000005 ...\" with a register dump, exit code 2")
	os.Stdout.Sync()
	// RtlMoveMemory (ntdll) writes to address 16: the access violation happens in Windows code.
	p := syscall.NewLazyDLL("kernel32.dll").NewProc("RtlMoveMemory")
	src := []byte{1, 2, 3, 4}
	p.Call(16, uintptr(unsafe.Pointer(&src[0])), 4)
	fmt.Println("FAIL crash-native: still running")
	os.Exit(1)
}

// ---- time, random, files

func checkTime() error {
	t0 := time.Now()
	time.Sleep(100 * time.Millisecond)
	d := time.Since(t0)
	if d < 90*time.Millisecond || d > 2*time.Second {
		return fmt.Errorf("Sleep(100ms) took %v", d)
	}
	var wg sync.WaitGroup
	var fired int32
	for i := 0; i < 500; i++ {
		wg.Add(1)
		time.AfterFunc(time.Duration(i%50)*time.Millisecond, func() { atomic.AddInt32(&fired, 1); wg.Done() })
	}
	wg.Wait()
	if _, err := time.LoadLocation("Local"); err != nil {
		return err
	}
	info("time", "Sleep(100ms) %v, zone %s, %d timers fired", d.Round(time.Millisecond), time.Now().Format("MST -0700"), fired)
	return nil
}

func checkRand() error {
	a := make([]byte, 1<<20)
	b := make([]byte, 1<<20)
	if _, err := rand.Read(a); err != nil {
		return err
	}
	if _, err := rand.Read(b); err != nil {
		return err
	}
	if sha256.Sum256(a) == sha256.Sum256(b) {
		return errors.New("two reads returned the same data")
	}
	var count [256]int
	for _, c := range a {
		count[c]++
	}
	for v, c := range count {
		// 4096 expected per value; this bound fails only for badly broken output
		if c < 3000 || c > 5200 {
			return fmt.Errorf("byte %d occurs %d times in 1 MiB", v, c)
		}
	}
	return nil
}

func checkFile() error {
	dir, err := os.MkdirTemp("", "nt50probe")
	if err != nil {
		return err
	}
	defer os.RemoveAll(dir)
	p := filepath.Join(dir, "a.txt")
	if err := os.WriteFile(p, []byte("hello"), 0o644); err != nil {
		return err
	}
	if err := os.Rename(p, p+".2"); err != nil {
		return err
	}
	b, err := os.ReadFile(p + ".2")
	if err != nil || string(b) != "hello" {
		return fmt.Errorf("read %q %v", b, err)
	}
	st, err := os.Stat(p + ".2")
	if err != nil || st.Size() != 5 {
		return fmt.Errorf("stat %v %v", st, err)
	}
	ents, err := os.ReadDir(dir)
	if err != nil || len(ents) != 1 {
		return fmt.Errorf("readdir %v %v", ents, err)
	}
	exe, err := os.Executable()
	if err != nil {
		return err
	}
	info("file", "executable %s", exe)
	return nil
}

// ---- network

func echoServer(l net.Listener) {
	for {
		c, err := l.Accept()
		if err != nil {
			return
		}
		go func() {
			defer c.Close()
			io.Copy(c, c)
		}()
	}
}

func roundTrip(addr string, size int) error {
	c, err := net.DialTimeout("tcp", addr, 5*time.Second)
	if err != nil {
		return err
	}
	defer c.Close()
	data := make([]byte, size)
	rand.Read(data)
	errc := make(chan error, 1)
	go func() {
		_, err := c.Write(data)
		errc <- err
	}()
	got := make([]byte, size)
	c.SetReadDeadline(time.Now().Add(20 * time.Second))
	if _, err := io.ReadFull(c, got); err != nil {
		return err
	}
	if err := <-errc; err != nil {
		return err
	}
	if sha256.Sum256(got) != sha256.Sum256(data) {
		return errors.New("echo differs")
	}
	return nil
}

func checkTCP() error {
	l, err := net.Listen("tcp", "127.0.0.1:0")
	if err != nil {
		return err
	}
	defer l.Close()
	go echoServer(l)
	return roundTrip(l.Addr().String(), 4<<20)
}

func checkTCPParallel() error {
	l, err := net.Listen("tcp", "127.0.0.1:0")
	if err != nil {
		return err
	}
	defer l.Close()
	go echoServer(l)
	errs := make(chan error, 64)
	for i := 0; i < 64; i++ {
		go func() { errs <- roundTrip(l.Addr().String(), 64<<10) }()
	}
	for i := 0; i < 64; i++ {
		if err := <-errs; err != nil {
			return err
		}
	}
	return nil
}

func checkTCPRefused() error {
	l, err := net.Listen("tcp", "127.0.0.1:0")
	if err != nil {
		return err
	}
	addr := l.Addr().String()
	l.Close()
	t0 := time.Now()
	c, err := net.DialTimeout("tcp", addr, 10*time.Second)
	if err == nil {
		c.Close()
		return errors.New("dial to a closed port succeeded")
	}
	// Decided by the Winsock error code, never by the message: Windows translates the message
	// (Russian XP: "Удаленная система отклонила запрос на подключение к сети.").
	if !isConnRefused(err) {
		return fmt.Errorf("unexpected error (want WSAECONNREFUSED %d): %v [%s]", wsaeconnrefused, err, errnoChain(err))
	}
	info("tcp-refused", "%v after %v [%s]", err, time.Since(t0).Round(time.Millisecond), errnoChain(err))
	return nil
}

// A dial to an address that does not answer must end with the context: by deadline and by cancel.
// (Without a route the system may refuse at once instead; that is reported, not failed.)
func checkTCPCancel() error {
	const blackhole = "10.255.255.1:9"
	var d net.Dialer
	ctx, cancel := context.WithTimeout(context.Background(), 700*time.Millisecond)
	t0 := time.Now()
	c, err := d.DialContext(ctx, "tcp", blackhole)
	cancel()
	el := time.Since(t0)
	if c != nil {
		c.Close()
		info("tcp-cancel", "%s answered; deadline not tested", blackhole)
		return nil
	}
	info("tcp-cancel", "deadline: %v after %v", err, el.Round(time.Millisecond))
	if el > 3*time.Second {
		return fmt.Errorf("deadline 700ms, returned after %v", el)
	}
	ctx, cancel = context.WithCancel(context.Background())
	time.AfterFunc(300*time.Millisecond, cancel)
	t0 = time.Now()
	c, err = d.DialContext(ctx, "tcp", blackhole)
	el = time.Since(t0)
	if c != nil {
		c.Close()
		return nil
	}
	info("tcp-cancel", "cancel: %v after %v", err, el.Round(time.Millisecond))
	if el > 3*time.Second {
		return fmt.Errorf("cancelled after 300ms, returned after %v", el)
	}
	return nil
}

func checkReadDeadline() error {
	l, err := net.Listen("tcp", "127.0.0.1:0")
	if err != nil {
		return err
	}
	defer l.Close()
	go func() {
		c, err := l.Accept()
		if err == nil {
			time.Sleep(2 * time.Second)
			c.Close()
		}
	}()
	c, err := net.Dial("tcp", l.Addr().String())
	if err != nil {
		return err
	}
	defer c.Close()
	c.SetReadDeadline(time.Now().Add(200 * time.Millisecond))
	t0 := time.Now()
	_, err = c.Read(make([]byte, 1))
	var ne net.Error
	if !errors.As(err, &ne) || !ne.Timeout() {
		return fmt.Errorf("read: %v", err)
	}
	if el := time.Since(t0); el > 1500*time.Millisecond {
		return fmt.Errorf("deadline 200ms, returned after %v", el)
	}
	return nil
}

func checkUDP() error {
	a, err := net.ListenPacket("udp", "127.0.0.1:0")
	if err != nil {
		return err
	}
	defer a.Close()
	b, err := net.Dial("udp", a.LocalAddr().String())
	if err != nil {
		return err
	}
	defer b.Close()
	if _, err := b.Write([]byte("ping")); err != nil {
		return err
	}
	buf := make([]byte, 16)
	a.SetReadDeadline(time.Now().Add(3 * time.Second))
	n, from, err := a.ReadFrom(buf)
	if err != nil || string(buf[:n]) != "ping" {
		return fmt.Errorf("read %q %v", buf[:n], err)
	}
	if _, err := a.WriteTo([]byte("pong"), from); err != nil {
		return err
	}
	b.SetReadDeadline(time.Now().Add(3 * time.Second))
	n, err = b.Read(buf)
	if err != nil || string(buf[:n]) != "pong" {
		return fmt.Errorf("reply %q %v", buf[:n], err)
	}
	return nil
}

func checkDNSLocal() error {
	addrs, err := net.LookupHost("localhost")
	if err != nil {
		return err
	}
	ok := false
	for _, a := range addrs {
		if a == "127.0.0.1" || a == "::1" {
			ok = true
		}
	}
	if !ok {
		return fmt.Errorf("localhost = %v", addrs)
	}
	host, _ := os.Hostname()
	if host != "" {
		if ips, err := net.LookupIP(host); err != nil {
			info("dns-local", "own name %s: %v", host, err)
		} else {
			info("dns-local", "own name %s: %v", host, ips)
		}
	}
	_, err = net.LookupHost("nt50probe-no-such-host.invalid")
	var de *net.DNSError
	if err == nil || !errors.As(err, &de) {
		return fmt.Errorf("unknown name: %v", err)
	}
	info("dns-local", "localhost %v; unknown name: %v (not found %v)", addrs, err, de.IsNotFound)
	return nil
}

func checkPortLookup() error {
	p, err := net.LookupPort("tcp", "http")
	if err != nil {
		return err
	}
	if p != 80 {
		return fmt.Errorf("http = %d", p)
	}
	return nil
}

// net.Interfaces needs GetAdaptersAddresses (Windows XP); on Windows 2000 it must fail cleanly.
func checkInterfaces() error {
	ifs, err := net.Interfaces()
	if err != nil {
		info("interfaces", "error (expected on Windows 2000): %v", err)
		return nil
	}
	info("interfaces", "%d interfaces", len(ifs))
	return nil
}

func checkDNSName(name string) error {
	t0 := time.Now()
	ips, err := net.LookupIP(name)
	if err != nil {
		return err
	}
	info("dns-remote", "%s = %v (%v)", name, ips, time.Since(t0).Round(time.Millisecond))
	return nil
}

// ---- TLS with certificate verification, against a local server with a CA made here

type testPKI struct {
	pool   *x509.CertPool
	server tls.Certificate
}

func newPKI(rsaKeys bool) (*testPKI, error) {
	newKey := func() (interface{}, interface{}, error) {
		if rsaKeys {
			k, err := rsa.GenerateKey(rand.Reader, 2048)
			if err != nil {
				return nil, nil, err
			}
			return k, &k.PublicKey, nil
		}
		k, err := ecdsa.GenerateKey(elliptic.P256(), rand.Reader)
		if err != nil {
			return nil, nil, err
		}
		return k, &k.PublicKey, nil
	}
	caKey, caPub, err := newKey()
	if err != nil {
		return nil, err
	}
	now := time.Now()
	caTmpl := &x509.Certificate{
		SerialNumber:          big.NewInt(1),
		Subject:               pkix.Name{CommonName: "nt50probe test CA"},
		NotBefore:             now.Add(-time.Hour),
		NotAfter:              now.Add(24 * time.Hour),
		IsCA:                  true,
		BasicConstraintsValid: true,
		KeyUsage:              x509.KeyUsageCertSign,
	}
	caDER, err := x509.CreateCertificate(rand.Reader, caTmpl, caTmpl, caPub, caKey)
	if err != nil {
		return nil, err
	}
	ca, err := x509.ParseCertificate(caDER)
	if err != nil {
		return nil, err
	}
	leafKey, leafPub, err := newKey()
	if err != nil {
		return nil, err
	}
	leafTmpl := &x509.Certificate{
		SerialNumber: big.NewInt(2),
		Subject:      pkix.Name{CommonName: "probe.test"},
		DNSNames:     []string{"probe.test"},
		NotBefore:    now.Add(-time.Hour),
		NotAfter:     now.Add(24 * time.Hour),
		KeyUsage:     x509.KeyUsageDigitalSignature | x509.KeyUsageKeyEncipherment,
		ExtKeyUsage:  []x509.ExtKeyUsage{x509.ExtKeyUsageServerAuth},
	}
	leafDER, err := x509.CreateCertificate(rand.Reader, leafTmpl, ca, leafPub, caKey)
	if err != nil {
		return nil, err
	}
	pool := x509.NewCertPool()
	pool.AddCert(ca)
	return &testPKI{pool: pool, server: tls.Certificate{Certificate: [][]byte{leafDER}, PrivateKey: leafKey}}, nil
}

func tlsDial(addr string, cfg *tls.Config) error {
	c, err := tls.DialWithDialer(&net.Dialer{Timeout: 10 * time.Second}, "tcp", addr, cfg)
	if err != nil {
		return err
	}
	defer c.Close()
	c.SetDeadline(time.Now().Add(10 * time.Second))
	if _, err := c.Write([]byte("hello\n")); err != nil {
		return err
	}
	line, err := bufio.NewReader(c).ReadString('\n')
	if err != nil || line != "hello\n" {
		return fmt.Errorf("echo %q %v", line, err)
	}
	return nil
}

func checkTLS() error {
	for _, rsaKeys := range []bool{false, true} {
		pki, err := newPKI(rsaKeys)
		if err != nil {
			return err
		}
		l, err := tls.Listen("tcp", "127.0.0.1:0", &tls.Config{Certificates: []tls.Certificate{pki.server}, MinVersion: tls.VersionTLS12})
		if err != nil {
			return err
		}
		go echoServer(l)
		addr := l.Addr().String()
		for _, v := range []uint16{tls.VersionTLS12, tls.VersionTLS13} {
			cfg := &tls.Config{RootCAs: pki.pool, ServerName: "probe.test", MinVersion: v, MaxVersion: v}
			if err := tlsDial(addr, cfg); err != nil {
				l.Close()
				return fmt.Errorf("rsa=%v version %x: %v", rsaKeys, v, err)
			}
		}
		// verification stays on: an unknown CA and a wrong name are refused
		err = tlsDial(addr, &tls.Config{RootCAs: x509.NewCertPool(), ServerName: "probe.test"})
		var ua x509.UnknownAuthorityError
		if !errors.As(err, &ua) {
			l.Close()
			return fmt.Errorf("unknown CA accepted or wrong error: %v", err)
		}
		err = tlsDial(addr, &tls.Config{RootCAs: pki.pool, ServerName: "other.test"})
		var he x509.HostnameError
		if !errors.As(err, &he) {
			l.Close()
			return fmt.Errorf("wrong name accepted or wrong error: %v", err)
		}
		l.Close()
	}
	return nil
}

func checkHTTPS(url, caFile string) error {
	pem, err := os.ReadFile(caFile)
	if err != nil {
		return err
	}
	pool := x509.NewCertPool()
	if !pool.AppendCertsFromPEM(pem) {
		return errors.New("no certificates in " + caFile)
	}
	tr := &http.Transport{TLSClientConfig: &tls.Config{RootCAs: pool}, Proxy: http.ProxyFromEnvironment}
	cl := &http.Client{Transport: tr, Timeout: 30 * time.Second}
	t0 := time.Now()
	resp, err := cl.Get(url)
	if err != nil {
		return err
	}
	defer resp.Body.Close()
	n, _ := io.Copy(io.Discard, io.LimitReader(resp.Body, 1<<20))
	cs := resp.TLS
	info("https-remote", "%s: %s, %d bytes, TLS %x, cipher %x, %v", url, resp.Status, n, cs.Version, cs.CipherSuite, time.Since(t0).Round(time.Millisecond))
	return nil
}

// ---- child process

func checkExec() error {
	exe, err := os.Executable()
	if err != nil {
		return err
	}
	out, err := exec.Command(exe, "child").Output()
	var ee *exec.ExitError
	if !errors.As(err, &ee) || ee.ExitCode() != 7 {
		return fmt.Errorf("child: %v", err)
	}
	if strings.TrimSpace(string(out)) != "child ok" {
		return fmt.Errorf("child output %q", out)
	}
	return nil
}
