// e2eserve runs the two local servers of the end-to-end test: a TLS 1.3 server for "dest.test"
// (the REALITY dest, self-signed) and a plain HTTP server that answers every request with a fixed
// body. Synthetic test data only.
//
//	e2eserve TLSADDR HTTPADDR
package main

import (
	"crypto/ecdsa"
	"crypto/elliptic"
	"crypto/rand"
	"crypto/tls"
	"crypto/x509"
	"crypto/x509/pkix"
	"fmt"
	"log"
	"math/big"
	"net"
	"net/http"
	"os"
	"time"
)

const Body = "nt50-e2e-ok\n"

func main() {
	if len(os.Args) != 3 {
		fmt.Println("usage: e2eserve TLSADDR HTTPADDR")
		os.Exit(2)
	}
	key, err := ecdsa.GenerateKey(elliptic.P256(), rand.Reader)
	if err != nil {
		log.Fatal(err)
	}
	tmpl := &x509.Certificate{
		SerialNumber: big.NewInt(1),
		Subject:      pkix.Name{CommonName: "dest.test"},
		DNSNames:     []string{"dest.test"},
		NotBefore:    time.Now().Add(-time.Hour),
		NotAfter:     time.Now().Add(24 * time.Hour),
		KeyUsage:     x509.KeyUsageDigitalSignature,
		ExtKeyUsage:  []x509.ExtKeyUsage{x509.ExtKeyUsageServerAuth},
	}
	der, err := x509.CreateCertificate(rand.Reader, tmpl, tmpl, &key.PublicKey, key)
	if err != nil {
		log.Fatal(err)
	}
	cert := tls.Certificate{Certificate: [][]byte{der}, PrivateKey: key}
	hs := &http.Server{Handler: http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		w.Write([]byte(Body))
	})}
	tl, err := tls.Listen("tcp", os.Args[1], &tls.Config{Certificates: []tls.Certificate{cert}, MinVersion: tls.VersionTLS13})
	if err != nil {
		log.Fatal(err)
	}
	go http.Serve(tl, hs.Handler)
	hl, err := net.Listen("tcp", os.Args[2])
	if err != nil {
		log.Fatal(err)
	}
	fmt.Println("e2eserve: tls", tl.Addr(), "http", hl.Addr())
	log.Fatal(hs.Serve(hl))
}
