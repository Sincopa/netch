package main

import (
    "net"
    "testing"
    "github.com/miekg/dns"
)

type recordingWriter struct { message *dns.Msg }
func (w *recordingWriter) LocalAddr() net.Addr { return &net.UDPAddr{} }
func (w *recordingWriter) RemoteAddr() net.Addr { return &net.UDPAddr{} }
func (w *recordingWriter) WriteMsg(m *dns.Msg) error { if m == nil { panic("nil DNS response") }; w.message = m; return nil }
func (w *recordingWriter) Write(b []byte) (int, error) { return len(b), nil }
func (w *recordingWriter) Close() error { return nil }
func (w *recordingWriter) TsigStatus() error { return nil }
func (w *recordingWriter) TsigTimersOnly(bool) {}
func (w *recordingWriter) Hijack() {}

func TestUpstreamFailureReturnsServFail(t *testing.T) {
    // Invalid addresses fail before any network request. Previously WriteMsg(nil) panicked.
    ChinaDNS, OtherDNS = "invalid-address", "invalid-address"
    for _, handler := range []func(dns.ResponseWriter, *dns.Msg){handleChinaDNS, handleOtherDNS} {
        request := new(dns.Msg)
        request.SetQuestion("example.invalid.", dns.TypeA)
        writer := new(recordingWriter)
        handler(writer, request)
        if writer.message == nil || writer.message.Rcode != dns.RcodeServerFailure || writer.message.Id != request.Id {
            t.Fatalf("expected a matching SERVFAIL reply, got %v", writer.message)
        }
    }
}
