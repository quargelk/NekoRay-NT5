package main

import (
	"compress/gzip"
	"crypto/ecdsa"
	"crypto/elliptic"
	"crypto/rand"
	"crypto/x509"
	"crypto/x509/pkix"
	"encoding/pem"
	"math/big"
	"net/http"
	"net/http/httptest"
	"os"
	"path/filepath"
	"strings"
	"testing"
	"time"
)

// All servers here are local (httptest) with their own test certificate; the system proxy and
// remote servers are not involved.

func serverCA(t *testing.T, srv *httptest.Server) string {
	t.Helper()
	path := filepath.Join(t.TempDir(), "test-ca.pem")
	data := pem.EncodeToMemory(&pem.Block{Type: "CERTIFICATE", Bytes: srv.Certificate().Raw})
	if err := os.WriteFile(path, data, 0600); err != nil {
		t.Fatal(err)
	}
	return path
}

// A freshly generated self-signed CA that signed nothing used here.
func unrelatedCA(t *testing.T) string {
	t.Helper()
	key, err := ecdsa.GenerateKey(elliptic.P256(), rand.Reader)
	if err != nil {
		t.Fatal(err)
	}
	tmpl := &x509.Certificate{
		SerialNumber:          big.NewInt(1),
		Subject:               pkix.Name{CommonName: "Unrelated test CA"},
		NotBefore:             time.Now().Add(-time.Hour),
		NotAfter:              time.Now().Add(time.Hour),
		IsCA:                  true,
		BasicConstraintsValid: true,
		KeyUsage:              x509.KeyUsageCertSign,
	}
	der, err := x509.CreateCertificate(rand.Reader, tmpl, tmpl, &key.PublicKey, key)
	if err != nil {
		t.Fatal(err)
	}
	path := filepath.Join(t.TempDir(), "unrelated-ca.pem")
	os.WriteFile(path, pem.EncodeToMemory(&pem.Block{Type: "CERTIFICATE", Bytes: der}), 0600)
	return path
}

func fetchTo(t *testing.T, args ...string) (int, string) {
	t.Helper()
	out := filepath.Join(t.TempDir(), "out.txt")
	code := runFetch(append(args, "-out", out))
	data, _ := os.ReadFile(out)
	return code, string(data)
}

func TestFetchHTTPSWithTrustedCA(t *testing.T) {
	srv := httptest.NewTLSServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		w.Write([]byte("vless://synthetic\n"))
	}))
	defer srv.Close()
	code, body := fetchTo(t, "-url", srv.URL+"/sub?token=synthetic", "-ca", serverCA(t, srv))
	if code != fetchOK || body != "vless://synthetic\n" {
		t.Fatalf("code %d body %q", code, body)
	}
}

// The address (with its token) can come in a file instead of the command line.
func TestFetchURLFromFile(t *testing.T) {
	var got string
	srv := httptest.NewTLSServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		got = r.URL.RawQuery
		w.Write([]byte("vless://from-file\n"))
	}))
	defer srv.Close()
	file := filepath.Join(t.TempDir(), "url.txt")
	os.WriteFile(file, []byte(srv.URL+"/sub?token=synthetic-file\r\n"), 0600)
	code, body := fetchTo(t, "-url-file", file, "-ca", serverCA(t, srv))
	if code != fetchOK || body != "vless://from-file\n" || got != "token=synthetic-file" {
		t.Fatalf("code %d body %q query %q", code, body, got)
	}
	if code, _ = fetchTo(t, "-url-file", filepath.Join(t.TempDir(), "missing.txt")); code != fetchUsage {
		t.Fatalf("missing address file: %d", code)
	}
}

func TestFetchRefusesUntrustedCertificate(t *testing.T) {
	srv := httptest.NewTLSServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		w.Write([]byte("must not be read"))
	}))
	defer srv.Close()
	// no -ca: the test certificate is not in the system store, so verification must fail
	code, body := fetchTo(t, "-url", srv.URL+"/sub")
	if code != fetchTLS || body != "" {
		t.Fatalf("untrusted certificate accepted: code %d body %q", code, body)
	}
	// a CA file with an unrelated certificate does not help either
	code, _ = fetchTo(t, "-url", srv.URL+"/sub", "-ca", unrelatedCA(t))
	if code != fetchTLS {
		t.Fatalf("certificate of another CA accepted: code %d", code)
	}
}

func TestFetchRefusesDowngradeRedirect(t *testing.T) {
	plain := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		w.Write([]byte("insecure"))
	}))
	defer plain.Close()
	srv := httptest.NewTLSServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		http.Redirect(w, r, plain.URL+"/x", http.StatusFound)
	}))
	defer srv.Close()
	code, body := fetchTo(t, "-url", srv.URL+"/sub", "-ca", serverCA(t, srv))
	if code == fetchOK || body != "" {
		t.Fatalf("https -> http redirect followed: code %d", code)
	}
}

func TestFetchFollowsHTTPSRedirect(t *testing.T) {
	var srv *httptest.Server
	srv = httptest.NewTLSServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		if r.URL.Path == "/old" {
			http.Redirect(w, r, "/new", http.StatusMovedPermanently)
			return
		}
		w.Write([]byte("moved"))
	}))
	defer srv.Close()
	code, body := fetchTo(t, "-url", srv.URL+"/old", "-ca", serverCA(t, srv))
	if code != fetchOK || body != "moved" {
		t.Fatalf("code %d body %q", code, body)
	}
}

func TestFetchSizeLimitAndStatus(t *testing.T) {
	srv := httptest.NewTLSServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		if r.URL.Path == "/missing" {
			http.NotFound(w, r)
			return
		}
		w.Write([]byte(strings.Repeat("x", 2000)))
	}))
	defer srv.Close()
	ca := serverCA(t, srv)
	if code, _ := fetchTo(t, "-url", srv.URL+"/big", "-ca", ca, "-max", "1000"); code != fetchTooLarge {
		t.Fatalf("size limit: code %d", code)
	}
	if code, _ := fetchTo(t, "-url", srv.URL+"/missing", "-ca", ca); code != fetchStatus {
		t.Fatalf("404: code %d", code)
	}
}

func TestFetchUsage(t *testing.T) {
	if code, _ := fetchTo(t, "-url", "ftp://example.invalid/x"); code != fetchUsage {
		t.Fatalf("ftp accepted: code %d", code)
	}
}

func TestFetchMissingOrDamagedCABundle(t *testing.T) {
	srv := httptest.NewTLSServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		w.Write([]byte("must not be read"))
	}))
	defer srv.Close()
	good, err := os.ReadFile(serverCA(t, srv))
	if err != nil {
		t.Fatal(err)
	}
	dir := t.TempDir()
	cases := map[string][]byte{
		"truncated.pem": good[:len(good)/2],
		"garbage.pem":   []byte("-----BEGIN CERTIFICATE-----\nAAAA\n-----END CERTIFICATE-----\n"),
		"empty.pem":     []byte("# no certificates here\n"),
		"partial.pem":   append(append([]byte{}, good...), good[:len(good)-40]...),
	}
	for name, data := range cases {
		path := filepath.Join(dir, name)
		os.WriteFile(path, data, 0600)
		if code, body := fetchTo(t, "-url", srv.URL+"/sub", "-ca", path); code != fetchCA || body != "" {
			t.Fatalf("%s: code %d body %q", name, code, body)
		}
	}
	if code, _ := fetchTo(t, "-url", srv.URL+"/sub", "-ca", filepath.Join(dir, "missing.pem")); code != fetchCA {
		t.Fatalf("missing bundle: code %d", code)
	}
}

func TestUnknownAuthorityMessageNamesTheBundle(t *testing.T) {
	srv := httptest.NewTLSServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {}))
	defer srv.Close()
	client, roots, _, err := newClient("", unrelatedCA(t), 5*time.Second)
	if err != nil {
		t.Fatal(err)
	}
	_, err = client.Get(srv.URL + "/sub?token=synthetic")
	if err == nil {
		t.Fatal("untrusted certificate accepted")
	}
	msg, code := describe(err, roots)
	if code != fetchTLS || !strings.Contains(msg, "unrelated-ca.pem") || strings.Contains(msg, "token=") {
		t.Fatalf("code %d message %q", code, msg)
	}
	_, store, _, _ := newClient("", "", 5*time.Second)
	if store.file != "" && !strings.HasSuffix(store.file, "cacert.pem") {
		t.Fatalf("unexpected default source %q", store.file)
	}
}

// The bundle shipped in ..\runtime must be a current, complete Mozilla extract.
func TestShippedCABundle(t *testing.T) {
	path := filepath.Join("..", "runtime", "cacert.pem")
	_, count, err := loadCA(path)
	if err != nil {
		t.Fatal(err)
	}
	data, _ := os.ReadFile(path)
	text := string(data)
	if count < 100 || !strings.Contains(text, "Certificate data from Mozilla as of") {
		t.Fatalf("%d certificates; not a Mozilla extract?", count)
	}
	for _, root := range []string{"ISRG Root X1", "DigiCert Global Root G2", "GTS Root R1", "USERTrust RSA Certification Authority"} {
		if !strings.Contains(text, root) {
			t.Fatalf("%s is missing from the bundle", root)
		}
	}
}

func TestFetchSendsHeadersAndReturnsMeta(t *testing.T) {
	srv := httptest.NewTLSServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		if r.Header.Get("X-Hwid") != "0123456789abcdef" || r.Header.Get("User-Agent") != "NekoRayNT5/1.0" {
			w.WriteHeader(http.StatusForbidden)
			return
		}
		w.Header().Set("Profile-Title", "base64:VGVzdA==")
		w.Header().Set("Subscription-Userinfo", "upload=1; download=2; total=3; expire=0")
		w.Header().Set("Set-Cookie", "secret=1")
		w.Header().Set("X-Hwid-Active", "true")
		w.Write([]byte("vless://x"))
	}))
	defer srv.Close()
	dir := t.TempDir()
	headers := filepath.Join(dir, "h.txt")
	os.WriteFile(headers, []byte("x-hwid: 0123456789abcdef\r\nx-device-os: Windows\r\n"), 0600)
	meta := filepath.Join(dir, "meta.txt")
	code, _ := fetchTo(t, "-url", srv.URL, "-ca", serverCA(t, srv), "-headers", headers, "-meta", meta, "-agent", "NekoRayNT5/1.0")
	if code != fetchOK {
		t.Fatalf("code %d", code)
	}
	data, _ := os.ReadFile(meta)
	text := string(data)
	if !strings.Contains(text, "status: 200") || !strings.Contains(text, "profile-title: base64:VGVzdA==") ||
		!strings.Contains(text, "subscription-userinfo: upload=1") || !strings.Contains(text, "x-hwid-active: true") ||
		strings.Contains(text, "secret") {
		t.Fatalf("meta: %q", text)
	}

	// a refusal still reports its headers; a forbidden header name is refused before any request
	os.WriteFile(headers, []byte("x-hwid: other\n"), 0600)
	code, _ = fetchTo(t, "-url", srv.URL, "-ca", serverCA(t, srv), "-headers", headers, "-meta", meta)
	data, _ = os.ReadFile(meta)
	if code != fetchStatus || !strings.Contains(string(data), "status: 403") {
		t.Fatalf("refusal: code %d meta %q", code, data)
	}
	os.WriteFile(headers, []byte("Host: evil\n"), 0600)
	if code, _ = fetchTo(t, "-url", srv.URL, "-ca", serverCA(t, srv), "-headers", headers); code != fetchUsage {
		t.Fatalf("Host header accepted: %d", code)
	}
}

func TestFetchDecompressesGzip(t *testing.T) {
	srv := httptest.NewTLSServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		if !strings.Contains(r.Header.Get("Accept-Encoding"), "gzip") {
			w.Write([]byte("plain"))
			return
		}
		w.Header().Set("Content-Encoding", "gzip")
		gz := gzip.NewWriter(w)
		gz.Write([]byte("vless://gzipped"))
		gz.Close()
	}))
	defer srv.Close()
	code, body := fetchTo(t, "-url", srv.URL, "-ca", serverCA(t, srv))
	if code != fetchOK || body != "vless://gzipped" {
		t.Fatalf("code %d body %q", code, body)
	}
}
