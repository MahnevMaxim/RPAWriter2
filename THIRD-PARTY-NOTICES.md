# Third-Party Notices

RPAWriter2 uses the third-party components listed below. Their license texts are
reproduced in this file as required by the respective licenses.

## Dependencies (redistributed with the VSIX)

| Component | Version | License | Copyright / Source |
|---|---|---|---|
| Newtonsoft.Json | 13.0.3, 13.0.4 | MIT | © 2007 James Newton-King |
| DocumentFormat.OpenXml | 3.0.2 | MIT | © Microsoft Corporation |
| NPOI | 2.7.6 | Apache License 2.0 | © NPOI contributors |
| Markdig | 1.4.2 | BSD-2-Clause | © Alexandre Mutel |
| Material.Icons | 3.0.2 | MIT | © SKProCH |
| NLog | 4.6.8 | BSD-3-Clause | © NLog Project |
| System.Data.SQLite.Core | 1.0.119 | Public Domain | SQLite / Hipp, Wyrick & Company |
| Oracle.ManagedDataAccess | 19.8.0 | Oracle Technology Network License Agreement (OTN) | © Oracle Corporation |

## Build/test-only dependencies (not redistributed)

| Component | Version | License |
|---|---|---|
| Microsoft.VisualStudio.SDK | 17.14.40265 | Microsoft (Visual Studio extension SDK terms) |
| Microsoft.VSSDK.BuildTools | 18.9.820 | Microsoft |
| Microsoft.NET.Test.Sdk | 17.11.1 | MIT |
| NUnit | 3.14.0 | MIT |
| NUnit3TestAdapter | 4.6.0 | MIT |

> **Oracle.ManagedDataAccess** is redistributed under the **Oracle Technology
> Network License Agreement (OTN)** — a proprietary, **non-OSI** license (not
> FUTC: the NuGet package's `licenseUrl` for 19.8.0 points to
> `distribution-license.html`). It allows redistributing the unmodified driver
> only as part of your application, and requires including the license text,
> charging no extra fees, not using Oracle trademarks, indemnifying Oracle,
> keeping proprietary notices, and complying with export controls. See the
> dedicated section below for the full text. Consider moving Oracle
> connectivity behind the optional `Providers` mechanism so the core remains
> purely permissive-licensed.

## MIT License

The following components are distributed under the MIT License:
Newtonsoft.Json, DocumentFormat.OpenXml, Material.Icons.

```
MIT License

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## BSD 2-Clause License (Markdig)

```
Copyright (c) 2018-2019, Alexandre Mutel
All rights reserved.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met:

1. Redistributions of source code must retain the above copyright notice, this
   list of conditions and the following disclaimer.

2. Redistributions in binary form must reproduce the above copyright notice,
   this list of conditions and the following disclaimer in the documentation
   and/or other materials provided with the distribution.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND
ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE FOR
ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
(INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON
ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
```

## BSD 3-Clause License (NLog)

```
Copyright (c) 2004-2021, NLog Project
All rights reserved.

Redistribution and use in source and binary forms, with or without modification,
are permitted provided that the following conditions are met:

1. Redistributions of source code must retain the above copyright notice,
   this list of conditions and the following disclaimer.
2. Redistributions in binary form must reproduce the above copyright notice,
   this list of conditions and the following disclaimer in the documentation
   and/or other materials provided with the distribution.
3. Neither the name of the copyright holder nor the names of its contributors
   may be used to endorse or promote products derived from this software without
   specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND
ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED.
IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT,
INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT
NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR
PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY,
WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE)
ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE
POSSIBILITY OF SUCH DAMAGE.
```

## Apache License 2.0 (NPOI)

```
Apache License
Version 2.0, January 2004
http://www.apache.org/licenses/

TERMS AND CONDITIONS FOR USE, REPRODUCTION, AND DISTRIBUTION

1. Definitions.

"License" shall mean the terms and conditions for use, reproduction, and
distribution as defined by Sections 1 through 9 of this document.

"Licensor" shall mean the copyright owner or entity authorized by the copyright
owner that is granting the License.

"Legal Entity" shall mean the union of the acting entity and all other entities
that control, are controlled by, or are under common control with that entity.
For the purposes of this definition, "control" means (i) the power, direct or
indirect, to cause the direction or management of such entity, whether by
contract or otherwise, or (ii) ownership of fifty percent (50%) or more of the
outstanding shares, or (iii) beneficial ownership of such entity.

"You" (or "Your") shall mean an individual or Legal Entity exercising
permissions granted by this License.

"Source" form shall mean the preferred form for making modifications, including
but not limited to software source code, documentation source, and configuration
files.

"Object" form shall mean any form resulting from mechanical transformation or
translation of a Source form, including but not limited to compiled object code,
generated documentation, and conversions to other media types.

"Work" shall mean the work of authorship, whether in Source or Object form, made
available under the License, as indicated by a copyright notice that is included
in or attached to the work (an example is provided in the Appendix below).

"Derivative Works" shall mean any work, whether in Source or Object form, that
is based on (or derived from) the Work and for which the editorial revisions,
annotations, elaborations, or other modifications represent, as a whole, an
original work of authorship. For the purposes of this License, Derivative Works
shall not include works that remain separable from, or merely link (or bind by
name) to the interfaces of, the Work and Derivative Works thereof.

"Contribution" shall mean any work of authorship, including the original version
of the Work and any modifications or additions to that Work or Derivative Works
thereof, that is intentionally submitted to Licensor for inclusion in the Work
by the copyright owner or by an individual or Legal Entity authorized to submit
on behalf of the copyright owner. For the purposes of this definition,
"submitted" means any form of electronic, verbal, or written communication sent
to the Licensor or its representatives, including but not limited to
communication on electronic mailing lists, source code control systems, and
issue tracking systems that are managed by, or on behalf of, the Licensor for
the purpose of discussing and improving the Work, but excluding communication
that is conspicuously marked or otherwise designated in writing by the copyright
owner as "Not a Contribution."

"Contributor" shall mean Licensor and any individual or Legal Entity on behalf
of whom a Contribution has been received by Licensor and subsequently
incorporated within the Work.

2. Grant of Copyright License. Subject to the terms and conditions of this
License, each Contributor hereby grants to You a perpetual, worldwide,
non-exclusive, no-charge, royalty-free, irrevocable copyright license to
reproduce, prepare Derivative Works of, publicly display, publicly perform,
sublicense, and distribute the Work and such Derivative Works in Source or
Object form.

3. Grant of Patent License. Subject to the terms and conditions of this License,
each Contributor hereby grants to You a perpetual, worldwide, non-exclusive,
no-charge, royalty-free, irrevocable (except as stated in this section) patent
license to make, have made, use, offer to sell, sell, import, and otherwise
transfer the Work, where such license applies only to those patent claims
licensable by such Contributor that are necessarily infringed by their
Contribution(s) alone or by combination of their Contribution(s) with the Work
to which such Contribution(s) was submitted. If You institute patent litigation
against any entity (including a cross-claim or counterclaim in a lawsuit)
alleging that the Work or a Contribution incorporated within the Work
constitutes direct or contributory patent infringement, then any patent licenses
granted to You under this License for that Work shall terminate as of the date
such litigation is filed.

4. Redistribution. You may reproduce and distribute copies of the Work or
Derivative Works thereof in any medium, with or without modifications, and in
Source or Object form, provided that You meet the following conditions:

(a) You must give any other recipients of the Work or Derivative Works a copy of
this License; and

(b) You must cause any modified files to carry prominent notices stating that
You changed the files; and

(c) You must retain, in the Source form of any Derivative Works that You
distribute, all copyright, patent, trademark, and attribution notices from the
Source form of the Work, excluding those notices that do not pertain to any part
of the Derivative Works; and

(d) If the Work includes a "NOTICE" text file as part of its distribution, then
any Derivative Works that You distribute must include a readable copy of the
attribution notices contained within such NOTICE file, excluding those notices
that do not pertain to any part of the Derivative Works, in at least one of the
following places: within a NOTICE text file distributed as part of the
Derivative Works; within the Source form or documentation, if provided along
with the Derivative Works; or, within a display generated by the Derivative
Works, if and wherever such third-party notices normally appear. The contents of
the NOTICE file are for informational purposes only and do not modify the
License. You may add Your own attribution notices within Derivative Works that
You distribute, alongside or as an addendum to the NOTICE text from the Work,
provided that such additional attribution notices cannot be construed as
modifying the License.

You may add Your own copyright statement to Your modifications and may provide
additional or different license terms and conditions for use, reproduction, or
distribution of Your modifications, or for any such Derivative Works as a whole,
provided Your use, reproduction, and distribution of the Work otherwise complies
with the conditions stated in this License.

5. Submission of Contributions. Unless You explicitly state otherwise, any
Contribution intentionally submitted for inclusion in the Work by You to the
Licensor shall be under the terms and conditions of this License, without any
additional terms or conditions. Notwithstanding the above, nothing herein shall
supersede or modify the terms of any separate license agreement you may have
executed with Licensor regarding such Contributions.

6. Trademarks. This License does not grant permission to use the trade names,
trademarks, service marks, or product names of the Licensor, except as required
for reasonable and customary use in describing the origin of the Work and
reproducing the content of the NOTICE file.

7. Disclaimer of Warranty. Unless required by applicable law or agreed to in
writing, Licensor provides the Work (and each Contributor provides its
Contributions) on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY
KIND, either express or implied, including, without limitation, any warranties or
conditions of TITLE, NON-INFRINGEMENT, MERCHANTABILITY, or FITNESS FOR A
PARTICULAR PURPOSE. You are solely responsible for determining the
appropriateness of using or redistributing the Work and assume any risks
associated with Your exercise of permissions under this License.

8. Limitation of Liability. In no event and under no legal theory, whether in
tort (including negligence), contract, or otherwise, unless required by
applicable law (such as deliberate and grossly negligent acts) or agreed to in
writing, shall any Contributor be liable to You for damages, including any
direct, indirect, special, incidental, or consequential damages of any character
arising as a result of this License or out of the use or inability to use the
Work (including but not limited to damages for loss of goodwill, work stoppage,
computer failure or malfunction, or any and all other commercial damages or
losses), even if such Contributor has been advised of the possibility of such
damages.

9. Accepting Warranty or Additional Liability. While redistributing the Work or
Derivative Works thereof, You may choose to offer, and charge a fee for,
acceptance of support, warranty, indemnity, or other liability obligations
and/or rights consistent with this License. However, in accepting such
obligations, You may act only on Your own behalf and on Your sole
responsibility, not on behalf of any other Contributor, and only if You agree to
indemnify, defend, and hold each Contributor harmless for any liability incurred
by, or claims asserted against, such Contributor by reason of your accepting any
such warranty or additional liability.

END OF TERMS AND CONDITIONS
```

## Oracle Technology Network License Agreement (Oracle.ManagedDataAccess)

`Oracle.ManagedDataAccess` 19.8.0 is redistributed under Oracle's **Oracle
Technology Network License Agreement (OTN)** — a proprietary license, **not** an
OSI-approved open-source license. Canonical text:
<https://www.oracle.com/downloads/licenses/distribution-license.html>.

Key obligations for redistribution (summary; the full text below governs):

- Redistribute only the **unmodified** Programs and Program Documentation, and
  only **as part of and included in your application**.
- **Include a copy of this Agreement** with any distribution.
- **Do not charge your end users any additional fees** for the use of the
  Programs.
- **Do not use Oracle's name, logos, or other trademarks** to market your
  application.
- **Defend and indemnify** Oracle and its affiliates and licensors against
  claims arising out of your distribution.
- **Do not remove** Oracle's or a licensor's proprietary markings/notices.
- Comply with **U.S. and applicable export-control** laws and regulations.
- No **reverse engineering, disassembly, or decompilation** (except as required
  by law for interoperability).

RPAWriter2 is distributed free of charge, which satisfies the "no additional
fees" condition. The full text below ships with the VSIX to satisfy the
"include this Agreement" condition.

The text below is reproduced from Oracle's canonical page for compliance
purposes; the canonical text at the URL above governs.

```
Oracle Technology Network License Agreement

Oracle is willing to authorize Your access to software associated with this
License Agreement ("Agreement") only upon the condition that You accept that
this Agreement governs Your use of the software. By selecting the "Accept
License Agreement" button or box (or the equivalent) or installing or using the
Programs You indicate Your acceptance of this Agreement and Your agreement, as
an authorized representative of Your company or organization (if being acquired
for use by an entity) or as an individual, to comply with the license terms that
apply to the software that You wish to download and access. If You are not
willing to be bound by this Agreement, do not select the "Accept License
Agreement" button or box (or the equivalent) and do not download or access the
software.

Definitions

"Oracle" refers to Oracle America, Inc. "You" and "Your" refers to (a) a company
or organization (each an "Entity") accessing the Programs, if use of the
Programs will be on behalf of such Entity; or (b) an individual accessing the
Programs, if use of the Programs will not be on behalf of an Entity.
"Contractors" refers to Your agents and contractors (including, without
limitation, outsourcers). "Program(s)" refers to Oracle software provided by
Oracle pursuant to this Agreement and any updates, error corrections, and/or
Program Documentation provided by Oracle. "Program Documentation" refers to
Program user manuals and Program installation manuals, if any. If available,
Program Documentation may be delivered with the Programs and/or may be accessed
from www.oracle.com/documentation. "Associated Product" refers to the Oracle
product(s), if any, and as identified in the Programs documentation or on the
Programs download site, with which the Programs are intended to enable or
enhance interoperation with Your application(s). "Separate Terms" refers to
separate license terms that are specified in the Program Documentation, readmes
or notice files and that apply to Separately Licensed Third Party Technology.
"Separately Licensed Third Party Technology" refers to third party technology
that is licensed under Separate Terms and not under the terms of this Agreement.

License Rights and Restrictions

Oracle grants You a nonexclusive, nontransferable, limited license to, subject
to the restrictions stated in this Agreement, (a) internally use the Programs
solely for the purposes of developing, testing, prototyping and demonstrating
Your applications, and running the Programs for Your own internal business
operations; and (b) redistribute unmodified Programs and Programs Documentation
pursuant to the Programs Redistribution section below. You may allow Your
Contractor(s) to use the Programs, provided they are acting on Your behalf to
exercise license rights granted in this Agreement and further provided that You
are responsible for their compliance with this Agreement in such use. You will
have a written agreement with Your Contractor(s) that strictly limits their
right to use the Programs and that otherwise protects Oracle's intellectual
property rights to the same extent as this Agreement. You may make copies of the
Programs to the extent reasonably necessary to exercise the license rights
granted in this Agreement. You may make one copy of the Programs for backup
purposes. Further, You may not:

- remove or modify any Program markings or any notice of Oracle's or a
  licensor's proprietary rights;
- make the Programs available in any manner to any third party for use in the
  third party's business operations (unless such access is expressly permitted
  for the specific Program license or, if applicable, in the separate license
  agreement with Oracle governing Your use of the Programs);
- cause or permit reverse engineering (unless required by law for
  interoperability), disassembly or decompilation of the Programs; and
- disclose results of any Program benchmark tests without Oracle's prior
  written consent.

Programs Redistribution

You may copy and distribute the unmodified Programs and Program Documentation as
part of and included in Your application, provided that You:

(a) do not charge Your end users any additional fees for the use of the
Programs; and
(b) distribute the Programs and Program Documentation only as part of and
included in Your application; and
(c) include this Agreement with any distribution of the Programs and Program
Documentation; and
(d) do not use Oracle's name, logos, or other trademarks to market Your
application; and
(e) agree to defend and indemnify Oracle and its affiliates and licensors from
and against any claims, damages, costs, liabilities, and expenses (including
reasonable attorney's fees) arising out of or related to Your distribution of
the Programs.

Ownership

Oracle or its licensors retain all ownership and intellectual property rights to
the Programs.

Separately Licensed Third Party Technology

Oracle may provide certain notices to You in Program Documentation, readmes or
notice files in connection with Oracle or third party technology provided as or
with the Programs. If specified in the Program Documentation, readmes or notice
files, such technology will be licensed to You under Separate Terms. Your rights
to use Separately Licensed Third Party Technology under Separate Terms are not
restricted in any way by the terms herein. For clarity, notwithstanding the
existence of a notice, third party technology that is not Separately Licensed
Third Party Technology shall be deemed part of the Programs licensed to You
under the terms of this Agreement.

Source Code for Open Source Software

For software that You receive from Oracle in binary form that is licensed under
an open source license that gives You the right to receive the source code for
that binary, You can obtain a copy of the applicable source code from
https://oss.oracle.com/sources/ or http://www.oracle.com/goto/opensourcecode. If
the source code for such software was not provided to You with the binary, You
can also receive a copy of the source code on physical media by submitting a
written request pursuant to the instructions in the "Written Offer for Source
Code" section of the latter website.

Export

Export laws and regulations of the United States and any other relevant local
export laws and regulations apply to the Programs. You agree that such export
control laws govern Your use of the Programs (including technical data) and any
services deliverables provided under this Agreement, and You agree to comply
with all such export laws and regulations (including "deemed export" and "deemed
re-export" regulations). You agree that no data, information and/or Programs
will be exported, directly or indirectly, in violation of these laws, or will be
used for any purpose prohibited by these laws including, without limitation,
nuclear, chemical, or biological weapons proliferation, or development of
missile technology.

Disclaimer of Warranties; Limitation of Liability

THE PROGRAMS ARE PROVIDED "AS IS" WITHOUT WARRANTY OF ANY KIND. ORACLE FURTHER
DISCLAIMS ALL WARRANTIES, EXPRESS AND IMPLIED, INCLUDING WITHOUT LIMITATION, ANY
IMPLIED WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE, OR
NONINFRINGEMENT. IN NO EVENT UNLESS REQUIRED BY APPLICABLE LAW WILL ORACLE BE
LIABLE TO YOU FOR DAMAGES, INCLUDING ANY GENERAL, SPECIAL, INCIDENTAL OR
CONSEQUENTIAL DAMAGES ARISING OUT OF THE USE OR INABILITY TO USE THE PROGRAM
(INCLUDING BUT NOT LIMITED TO LOSS OF DATA OR DATA BEING RENDERED INACCURATE OR
LOSSES SUSTAINED BY YOU OR THIRD PARTIES OR A FAILURE OF THE PROGRAM TO OPERATE
WITH ANY OTHER PROGRAMS), EVEN IF SUCH HOLDER OR OTHER PARTY HAS BEEN ADVISED OF
THE POSSIBILITY OF SUCH DAMAGES. ORACLE'S TOTAL LIABILITY TO YOU FOR ALL
DAMAGES, LOSSES AND CAUSES OF ACTION (WHETHER IN CONTRACT, TORT OR OTHERWISE)
SHALL NOT EXCEED ONE THOUSAND DOLLARS (US$1,000).

No Technical Support

Unless Oracle support for the Programs, if any, is expressly included in a
separate, current support agreement between You and Oracle, Oracle's technical
support organization will not provide technical support, phone support, or
updates to You for the Programs provided under this Agreement.

Audit; Termination

Oracle may audit Your use of the Programs. You may terminate this Agreement by
destroying all copies of the Programs. This Agreement shall automatically
terminate without notice if You fail to comply with any of the terms of this
Agreement, in which case You shall promptly destroy all copies of the Programs.

U.S. Government End Users

Programs and/or Programs Documentation delivered to U.S. Government end users
are "commercial computer software" pursuant to the applicable Federal
Acquisition Regulation and agency-specific supplemental regulations. As such,
use, duplication, disclosure, modification, and adaptation of the Programs
and/or Programs Documentation shall be subject to the license terms and license
restrictions set forth in this Agreement. No other rights are granted to the
U.S. Government.

Relationship Between the Parties

Oracle is an independent contractor and we agree that no partnership, joint
venture, or agency relationship exists between us. We each will be responsible
for paying our own employees, including employment related taxes and insurance.
Nothing in this agreement shall be construed to limit either party's right to
independently develop or distribute software that is functionally similar to the
other party's products, so long as proprietary information of the other party is
not included in such software.

Entire Agreement; Governing Law

You agree that this Agreement is the complete agreement for the Programs and
this Agreement supersedes all prior or contemporaneous agreements or
representations, including any clickwrap, shrinkwrap or similar licenses, or
license agreements for prior versions of the Programs. This Agreement may not be
modified and the rights and restrictions may not be altered or waived except in
a writing signed by authorized representatives of You and of Oracle. If any term
of this Agreement is found to be invalid or unenforceable, the remaining
provisions will remain effective. This Agreement is governed by the substantive
and procedural laws of the State of California, USA, and You and Oracle agree to
submit to the exclusive jurisdiction of, and venue in, the courts of San
Francisco or Santa Clara counties in California in any dispute arising out of or
relating to this Agreement.

Notices

Should you have any questions concerning this License Agreement, or if you
desire to contact Oracle for any reason, please write:

Oracle America, Inc.
500 Oracle Parkway
Redwood City, CA 94065

Last updated: 30 November 2016
```
