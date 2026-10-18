package main

import (
	"crypto/tls"
	"crypto/x509"
	"encoding/pem"
	"errors"
	"flag"
	"fmt"
	"io"
	"net"
	"net/http"
	"net/url"
	"os"
	"path/filepath"
	"strings"
	"time"
)

// Exit codes of "fetch".
const (
	fetchOK       = 0
	fetchUsage    = 2
	fetchNetwork  = 3
	fetchStatus   = 4
	fetchTooLarge = 5
	fetchTLS      = 6
	fetchIO       = 7
	fetchCA       = 8
)

const maxRedirects = 5

var errDowngrade = errors.New("the server redirected from HTTPS to an insecure address; refused")

// Certificate verification is never switched off. Trusted roots come from -ca, else from
// "cacert.pem" next to the program, else from the Windows certificate store (outdated on XP).
// A CA file that is missing or damaged is an error (exit code 8), never a reason to trust more.
func runFetch(args []string) int {
	fs := flag.NewFlagSet("fetch", flag.ContinueOnError)
	fs.SetOutput(io.Discard)
	rawURL := fs.String("url", "", "")
	out := fs.String("out", "", "")
	max := fs.Int64("max", 5<<20, "")
	timeout := fs.Int("timeout", 30, "")
	proxy := fs.String("proxy", "", "")
	ca := fs.String("ca", "", "")
	headersFile := fs.String("headers", "", "")
	metaFile := fs.String("meta", "", "")
	agent := fs.String("agent", "NekoRayNT5", "")
	// The address usually carries an access token: NekoRay NT5 passes it in a file, not on the
	// command line, which other programs of the computer can read.
	urlFile := fs.String("url-file", "", "")
	parsed := fs.Parse(args)
	if parsed == nil && *urlFile != "" {
		data, err := os.ReadFile(*urlFile)
		if err != nil {
			fmt.Fprintln(os.Stderr, "fetch: cannot read the address file")
			return fetchUsage
		}
		*rawURL = strings.TrimSpace(string(data))
	}
	if err := parsed; err != nil || *rawURL == "" || *out == "" || *max <= 0 || *timeout <= 0 {
		fmt.Fprintln(os.Stderr, "fetch: missing or invalid arguments")
		return fetchUsage
	}
	target, err := url.Parse(*rawURL)
	if err != nil || (target.Scheme != "https" && target.Scheme != "http") || target.Host == "" {
		fmt.Fprintln(os.Stderr, "fetch: the address must be an http:// or https:// URL")
		return fetchUsage
	}

	client, roots, code, err := newClient(*proxy, *ca, time.Duration(*timeout)*time.Second)
	if err != nil {
		fmt.Fprintln(os.Stderr, "fetch: "+err.Error())
		return code
	}
	req, err := http.NewRequest("GET", target.String(), nil)
	if err != nil {
		fmt.Fprintln(os.Stderr, "fetch: invalid address")
		return fetchUsage
	}
	req.Header.Set("User-Agent", *agent)
	if *headersFile != "" {
		// Extra headers (for example a device id) come in a file, so that they never appear in the
		// command line of the process; the file is deleted by the caller.
		extra, err := readHeaders(*headersFile)
		if err != nil {
			fmt.Fprintln(os.Stderr, "fetch: "+err.Error())
			return fetchUsage
		}
		for _, h := range extra {
			req.Header.Set(h[0], h[1])
		}
	}
	resp, err := client.Do(req)
	if err != nil {
		msg, code := describe(err, roots)
		fmt.Fprintln(os.Stderr, "fetch: "+msg)
		return code
	}
	defer resp.Body.Close()
	if *metaFile != "" {
		// written before the status check: a refusal may come with an explanation in the headers
		if err := writeMeta(*metaFile, resp); err != nil {
			fmt.Fprintln(os.Stderr, "fetch: cannot write the header file: "+err.Error())
			return fetchIO
		}
	}
	if resp.StatusCode != http.StatusOK {
		fmt.Fprintf(os.Stderr, "fetch: the server answered HTTP %d\n", resp.StatusCode)
		return fetchStatus
	}
	if resp.ContentLength > *max {
		fmt.Fprintf(os.Stderr, "fetch: the answer is larger than %d bytes\n", *max)
		return fetchTooLarge
	}
	data, err := io.ReadAll(io.LimitReader(resp.Body, *max+1))
	if err != nil {
		msg, code := describe(err, roots)
		fmt.Fprintln(os.Stderr, "fetch: reading the answer failed: "+msg)
		return code
	}
	if int64(len(data)) > *max {
		fmt.Fprintf(os.Stderr, "fetch: the answer is larger than %d bytes\n", *max)
		return fetchTooLarge
	}
	tmp := *out + ".part"
	if err := os.WriteFile(tmp, data, 0600); err != nil {
		fmt.Fprintln(os.Stderr, "fetch: cannot write the output file: "+err.Error())
		return fetchIO
	}
	if err := os.Rename(tmp, *out); err != nil {
		os.Remove(tmp)
		fmt.Fprintln(os.Stderr, "fetch: cannot write the output file: "+err.Error())
		return fetchIO
	}
	fmt.Printf("OK bytes=%d type=%s\n", len(data), strings.ReplaceAll(resp.Header.Get("Content-Type"), " ", ""))
	return fetchOK
}

// rootsSource tells where trusted roots came from, for the error messages.
type rootsSource struct {
	file  string // empty: the Windows certificate store
	certs int
}

// loadCA reads a PEM bundle strictly: every block must be a readable certificate, so that a
// truncated or damaged file is reported instead of silently trusting only part of it.
func loadCA(path string) (*x509.CertPool, int, error) {
	data, err := os.ReadFile(path)
	if err != nil {
		if os.IsNotExist(err) {
			return nil, 0, fmt.Errorf("the CA bundle %s is missing", filepath.Base(path))
		}
		return nil, 0, fmt.Errorf("cannot read the CA bundle %s: %v", filepath.Base(path), err)
	}
	pool := x509.NewCertPool()
	count := 0
	rest := data
	for {
		var block *pem.Block
		block, rest = pem.Decode(rest)
		if block == nil {
			break
		}
		if block.Type != "CERTIFICATE" {
			continue
		}
		cert, err := x509.ParseCertificate(block.Bytes)
		if err != nil {
			return nil, 0, fmt.Errorf("the CA bundle %s is damaged (certificate %d cannot be read)", filepath.Base(path), count+1)
		}
		pool.AddCert(cert)
		count++
	}
	if strings.Contains(string(rest), "-----BEGIN") {
		return nil, 0, fmt.Errorf("the CA bundle %s is damaged (incomplete certificate after %d good ones)", filepath.Base(path), count)
	}
	if count == 0 {
		return nil, 0, fmt.Errorf("the CA bundle %s contains no certificates", filepath.Base(path))
	}
	return pool, count, nil
}

// defaultCA is "cacert.pem" next to the program, or "" when there is none.
func defaultCA() string {
	exe, err := os.Executable()
	if err != nil {
		return ""
	}
	candidate := filepath.Join(filepath.Dir(exe), "cacert.pem")
	if _, err := os.Stat(candidate); err != nil {
		return ""
	}
	return candidate
}

func newClient(proxy, caFile string, timeout time.Duration) (*http.Client, rootsSource, int, error) {
	tlsConf := &tls.Config{MinVersion: tls.VersionTLS12}
	var roots rootsSource
	if caFile == "" {
		caFile = defaultCA()
	}
	if caFile != "" {
		pool, count, err := loadCA(caFile)
		if err != nil {
			return nil, roots, fetchCA, err
		}
		tlsConf.RootCAs = pool
		roots = rootsSource{file: filepath.Base(caFile), certs: count}
	}
	tr := &http.Transport{
		// Never the proxy of the environment: only the one the caller asks for.
		Proxy:                 nil,
		TLSClientConfig:       tlsConf,
		DisableKeepAlives:     true,
		TLSHandshakeTimeout:   timeout,
		ResponseHeaderTimeout: timeout,
		DialContext:           (&net.Dialer{Timeout: timeout}).DialContext,
	}
	if proxy != "" {
		p, err := url.Parse(proxy)
		if err != nil || p.Scheme != "http" || p.Host == "" {
			return nil, roots, fetchUsage, errors.New("the proxy must be http://HOST:PORT")
		}
		tr.Proxy = http.ProxyURL(p)
	}
	return &http.Client{
		Transport: tr,
		Timeout:   timeout,
		CheckRedirect: func(req *http.Request, via []*http.Request) error {
			if len(via) >= maxRedirects {
				return errors.New("too many redirects")
			}
			if via[len(via)-1].URL.Scheme == "https" && req.URL.Scheme != "https" {
				return errDowngrade
			}
			return nil
		},
	}, roots, fetchOK, nil
}

// describe turns an error into a message without the URL, and an exit code.
func describe(err error, roots rootsSource) (string, int) {
	var ue *url.Error
	if errors.As(err, &ue) {
		err = ue.Err
	}
	if errors.Is(err, errDowngrade) {
		return errDowngrade.Error(), fetchStatus
	}
	var unknown x509.UnknownAuthorityError
	var hostErr x509.HostnameError
	var invalid x509.CertificateInvalidError
	switch {
	case errors.As(err, &unknown):
		if roots.file == "" {
			return "the server certificate is not trusted by the Windows certificate store (unknown certificate authority), " +
				"and there is no cacert.pem next to nt5helper.exe. The root store of Windows XP is outdated: put the " +
				"current Mozilla CA bundle (cacert.pem) next to the program", fetchTLS
		}
		return fmt.Sprintf("the server certificate is not trusted (unknown certificate authority): it is not issued by "+
			"any of the %d public CAs in %s. Either the server uses a private certificate, or %s is outdated "+
			"(replace it with the current Mozilla CA bundle)", roots.certs, roots.file, roots.file), fetchTLS
	case errors.As(err, &hostErr):
		return "the server certificate is for another name: " + hostErr.Error(), fetchTLS
	case errors.As(err, &invalid):
		return "the server certificate is not valid: " + invalid.Error(), fetchTLS
	}
	var netErr net.Error
	if errors.As(err, &netErr) && netErr.Timeout() {
		return "the server did not answer in time", fetchNetwork
	}
	msg := err.Error()
	if strings.Contains(msg, "tls:") || strings.Contains(msg, "x509:") {
		return msg, fetchTLS
	}
	return msg, fetchNetwork
}

// readHeaders reads "Name: value" lines. Names must be plain tokens; Host and the hop-by-hop
// headers cannot be set this way.
func readHeaders(path string) ([][2]string, error) {
	data, err := os.ReadFile(path)
	if err != nil {
		return nil, errors.New("cannot read the header file")
	}
	var result [][2]string
	for _, line := range strings.Split(strings.ReplaceAll(string(data), "\r\n", "\n"), "\n") {
		if strings.TrimSpace(line) == "" {
			continue
		}
		colon := strings.IndexByte(line, ':')
		if colon <= 0 {
			return nil, errors.New("invalid line in the header file")
		}
		name := strings.TrimSpace(line[:colon])
		value := strings.TrimSpace(line[colon+1:])
		for _, c := range name {
			if !(c == '-' || (c >= '0' && c <= '9') || (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z')) {
				return nil, errors.New("invalid header name in the header file")
			}
		}
		switch strings.ToLower(name) {
		case "host", "connection", "content-length", "transfer-encoding", "proxy-authorization":
			return nil, errors.New("header " + name + " cannot be set")
		}
		for _, c := range value {
			if c < ' ' || c == 0x7f {
				return nil, errors.New("invalid header value in the header file")
			}
		}
		result = append(result, [2]string{name, value})
	}
	return result, nil
}

// metaHeaders are the response headers passed back to the program: the status, the content type,
// and what subscription panels report about the subscription. Nothing else (cookies etc.).
var metaHeaders = []string{
	"Content-Type", "Content-Disposition", "Profile-Title", "Subscription-Userinfo",
	"Profile-Update-Interval", "Profile-Web-Page-Url", "Support-Url", "Announce", "Providerid",
}

func writeMeta(path string, resp *http.Response) error {
	var sb strings.Builder
	fmt.Fprintf(&sb, "status: %d\n", resp.StatusCode)
	clean := func(v string) string {
		return strings.Map(func(r rune) rune {
			if r < ' ' || r == 0x7f {
				return ' '
			}
			return r
		}, v)
	}
	for _, name := range metaHeaders {
		if v := resp.Header.Get(name); v != "" {
			fmt.Fprintf(&sb, "%s: %s\n", strings.ToLower(name), clean(v))
		}
	}
	for name, values := range resp.Header {
		if strings.HasPrefix(strings.ToLower(name), "x-hwid") && len(values) > 0 {
			fmt.Fprintf(&sb, "%s: %s\n", strings.ToLower(name), clean(values[0]))
		}
	}
	return os.WriteFile(path, []byte(sb.String()), 0600)
}
