from dnslib import DNSRecord

def parse_dns_packet(packet):

    data = bytes.fromhex(packet)

    dns = DNSRecord.parse(data)

    print("header: ", dns.header)
    print("questions: ", dns.questions)
    print("rr: ", dns.rr)
    print("auth: ", dns.auth)
    print("ar: ", dns.ar)


if __name__ == "__main__":
    while True:
        packet = input("Enter DNS packet in hex: ")
        #strip out any spaces
        packet = packet.replace(" ", "")
        parse_dns_packet(packet)

