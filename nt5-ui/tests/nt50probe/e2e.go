package main

import (
	"fmt"
	"io"
	"net/http"
	"net/url"
	"os"
	"os/exec"
	"path/filepath"
	"strings"
	"time"
)

// e2e DIR: the local VLESS + REALITY + Vision test without network and without curl. Starts, from
// DIR, e2eserve.exe (REALITY dest + HTTP target), xray.exe with e2e\server.json and xray.exe with
// e2e\client.json, sends requests through the client's SOCKS5 (127.0.0.1:18100) and HTTP
// (127.0.0.1:18101) inbounds, then stops exactly these child processes. Synthetic keys only.
func runE2E(dir, xrayPath string) {
	header()
	// absolute: exec does not run programs from the current directory by a relative name
	if abs, err := filepath.Abs(dir); err == nil {
		dir = abs
	}
	xray := filepath.Join(dir, "xray.exe")
	if xrayPath != "" {
		xray = xrayPath // the xray.exe of the application package under test
	}
	var children []*exec.Cmd
	start := func(logName, name string, args ...string) {
		cmd := exec.Command(name, args...)
		logf, err := os.Create(filepath.Join(dir, "e2e", logName))
		if err == nil {
			cmd.Stdout, cmd.Stderr = logf, logf
		}
		if err := cmd.Start(); err != nil {
			fail("e2e-start", "%s: %v", name, err)
			return
		}
		children = append(children, cmd)
	}
	start("e2eserve.log", filepath.Join(dir, "e2eserve.exe"), "127.0.0.1:18443", "127.0.0.1:18080")
	start("server.log", xray, "run", "-c", filepath.Join(dir, "e2e", "server.json"))
	start("client.log", xray, "run", "-c", filepath.Join(dir, "e2e", "client.json"))
	time.Sleep(4 * time.Second)
	check("e2e-socks5", func() error { return getThrough("socks5://127.0.0.1:18100", "http://127.0.0.1:18080/") })
	check("e2e-socks5-name", func() error { return getThrough("socks5://127.0.0.1:18100", "http://localhost:18080/") })
	check("e2e-http-proxy", func() error { return getThrough("http://127.0.0.1:18101", "http://127.0.0.1:18080/") })
	check("e2e-parallel", func() error {
		errs := make(chan error, 20)
		for i := 0; i < 20; i++ {
			go func() { errs <- getThrough("socks5://127.0.0.1:18100", "http://127.0.0.1:18080/") }()
		}
		for i := 0; i < 20; i++ {
			if err := <-errs; err != nil {
				return err
			}
		}
		return nil
	})
	for _, c := range children {
		c.Process.Kill()
		c.Wait()
	}
	info("e2e", "logs in %s", filepath.Join(dir, "e2e"))
}

func getThrough(proxy, target string) error {
	pu, err := url.Parse(proxy)
	if err != nil {
		return err
	}
	cl := &http.Client{Transport: &http.Transport{Proxy: http.ProxyURL(pu)}, Timeout: 20 * time.Second}
	resp, err := cl.Get(target)
	if err != nil {
		return err
	}
	defer resp.Body.Close()
	b, err := io.ReadAll(io.LimitReader(resp.Body, 1<<16))
	if err != nil {
		return err
	}
	if strings.TrimSpace(string(b)) != "nt50-e2e-ok" {
		return fmt.Errorf("%s: answer %q", resp.Status, b)
	}
	return nil
}
