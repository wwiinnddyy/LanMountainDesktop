@echo off
cd /d "d:\github\LanMountainDesktop"
git --no-pager log --since="2026-05-31" --until="2026-05-31 23:59:59" --format="%%H|%%an|%%ae|%%ai|%%s" --no-merges
