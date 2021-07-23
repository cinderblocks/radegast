```
______          _                      _   
| ___ \        | |                    | |  
| |_/ /__ _  __| | ___  __ _  __ _ ___| |_ 
|    // _` |/ _` |/ _ \/ _` |/ _` / __| __|
| |\ \ (_| | (_| |  __/ (_| | (_| \__ \ |_ 
\_| \_\__,_|\__,_|\___|\__, |\__,_|___/\__|
                        __/ |              
                       |___/ 
```
Radegast Metaverse Client (http://radegast.life/)

[![License: LGPL v3](https://img.shields.io/badge/License-LGPL%20v3-blue.svg)](https://www.gnu.org/licenses/lgpl-3.0)

[![Build status](https://ci.appveyor.com/api/projects/status/g34olv3opd2vho32/branch/master?svg=true)](https://ci.appveyor.com/project/cinderblocks57647/radegast/branch/master)

[![Codacy Badge](https://api.codacy.com/project/badge/Grade/98684cbe995245f7ad1781cf254a742c)](https://www.codacy.com/manual/cinderblocks/radegast?utm_content=cinderblocks/radegast)

## Getting started

Radegast is a virtual world client compatible with Second Life and OpenSimulator.

### Prerequisites

Radegast requires .NET Framework 4.7.2 or compatible Mono version to build and run.

### Contributing

Pull requests are nice.

## Authors

### Project founder:

* **Latif Khalifa**

### Current maintainer and lead developer:

* **Cinder Roxley** (email cinder cinderblocks.biz)

### Contributors:

* **nooperation**
* **Luminous Luminos**

### Alumni Contributors:

* **Douglas R. Miles**
* **Mojito Sorbet**
* **Robin Cornelius**
* **Revolution Smythe**

## License

**Radegast Metaverse Client**
* Copyright © 2009-2018, Radegast Development Team
* Copyright © 2017-2020, Sjofn, LLC.
* All rights reserved.

 Radegast is free software: you can redistribute it and/or modify it under the terms of the GNU Lesser General Public License as published by the Free Software Foundation, either version 3 of the License, or (at your option) any later version.

 This program is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; without even the implied warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU General Public License for more details.

## MacOS Install (experimental)

1. Download the Linux/Mac Tarbal file from: https://radegast.life/downloads/ and decompress it in a directory of your choice.
2. Using Homebrew (https://brew.sh/) install the cask instead of the brew formula (the formula seems not to include the 32 bit code needed):
  `brew cask install mono-mdk`
3. Navigate to the directory you choosed in step 1, and launch the application with the --arch=32 option: 
  `mono --arch=32 Radegast.exe`
4. It will take a minute to start, but it should launch.

## Acknowledgments

Based on SLeek
Copyright © 2006-2008, Paul Clement (a.k.a. Delta)
All rights reserved.

Artwork files are licenced under the
Creative Commons Share and Share Alike 3.0
Copyright © 2002-2009 Linden Research Inc.

PrimMesher
Copyright © 2010 Dahlia Trimble
http://forge.opensimulator.org/gf/project/primmesher/

FMOD Library (FMOD Non Commercial License)
FMODStudio SoundSystem Copyright © 2005-2009 Firelight Technologies Pty, Ltd.
Redistributions in binary form must follow the following rules:
* The FMOD library cannot be used for resale or other commercial distribution
* This license cannot be used for products which do not make profit but are still commercially released
* This license cannot be used for commercial services, where the executable containing fmod is not sold, but the data is
(If you want to redistribute Radegast commercially, remove fmod.dll, fmod*.so and fmod*.dylib)

Ogg Vorbis
Copyright © 2002, Xiph.org Foundation

log4net
Copyright © Apache Software Foundation

LSL parser
Copyright © 2009 Contributors, http://opensimulator.org/

OpenTK 3D
Copyright © 2006-2014 Stefanos Apostolopoulos <stapostol@gmail.com> for the Open Toolkit library.

Command Line Library
Copyright © 2005-2010 Giacomo Stelluti Scala

CSAT Library
Copyright © 2011 mjt
http://code.google.com/p/csat/

protobuf-net
Copyright © Google Inc.
Copyright © 2008 Marc Gravell

zlib.net
Copyright © 1996-2017 Greg Roelofs, Jean-loup Gailly and Mark Adler.

AIMLBot
The AIMLBot library (Program#) is a .NET implementation of the AIML standard.
Copyright © 2006 Nicholas H.Tollervey (http://ntoll.org)
http://aimlbot.sourceforge.net/

SmartIrc4net
Copyright © 2003-2005 Mirco Bauer
http://www.meebey.net/projects/smartirc4net/
