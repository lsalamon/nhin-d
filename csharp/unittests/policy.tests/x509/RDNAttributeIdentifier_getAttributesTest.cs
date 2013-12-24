﻿/* 
 Copyright (c) 2013, Direct Project
 All rights reserved.

 Authors:
    Joe Shook      jshook@kryptiq.com
  
Redistribution and use in source and binary forms, with or without modification, are permitted provided that the following conditions are met:

Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer.
Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following disclaimer in the documentation and/or other materials provided with the distribution.
Neither the name of The Direct Project (directproject.org) nor the names of its contributors may be used to endorse or promote products derived from this software without specific prior written permission.
THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 
*/


using System;
using System.Security.Cryptography.X509Certificates;
using FluentAssertions;
using Health.Direct.Policy.X509;
using Health.Direct.Policy.X509.Standard;
using Xunit;

namespace Health.Direct.Policy.Tests.x509
{
    public class RdnAttributeIdentifier_GetAttributesTest
    {
        [Fact]
        public void testGetAttributes_toString()
        {
            RDNAttributeIdentifier.COMMON_NAME.Name.Should().Be("CN");
            RDNAttributeIdentifier.COUNTRY.Name.Should().Be("C");
        }

        [Fact]
        public void testGetAttributes_fromName()
        {
            RDNAttributeIdentifier.COMMON_NAME.Name.Should().Be(RDNAttributeIdentifier.FromName("CN").Name);
            RDNAttributeIdentifier.COMMON_NAME.OID.Should().Be(RDNAttributeIdentifier.FromName("CN").OID);
            RDNAttributeIdentifier.FromName("CN.").Should().BeNull();
            RDNAttributeIdentifier.COUNTRY.Name.Should().Be("C");
        }
    }
}
