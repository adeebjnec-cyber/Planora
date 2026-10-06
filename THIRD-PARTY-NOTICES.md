# Third-party software

Planora uses the following open-source software.

| Component | Used for | Licence |
|---|---|---|
| [MPXJ / MPXJ.Net](https://www.mpxj.org/) | Reading Microsoft Project `.mpp` files and other schedulers' formats; exporting to MPX and Primavera P6 | GNU Lesser General Public License v2.1 |
| [IKVM](https://github.com/ikvmnet/ikvm) | Running MPXJ (a Java library) on .NET | MIT |
| Libraries bundled with MPXJ (Apache POI, Jackson, Apache Commons, SQLite JDBC, Jackcess, and others) | Used internally by MPXJ to read file formats | Apache 2.0 / MIT / BSD (see each project) |

MPXJ is linked dynamically and is not modified, as the LGPL permits for use by applications under other licences.
