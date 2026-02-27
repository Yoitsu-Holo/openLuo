.PHONY: run test build publish format format-csharp format-python clean

run:
	dotnet run --project openLuo

test:
	dotnet test openLuo.Tests

build:
	dotnet build -c Release

publish:
	dotnet publish openLuo -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o ./publish/linux-x64
	cp -r openLuo/data ./publish/linux-x64/
	rm -f ./publish/linux-x64/config/*.example.json
	cp -r openLuo/data/config/. ./publish/linux-x64/config/
	cp -r openLuo/native ./publish/linux-x64/
	dotnet publish openLuo -c Release -r win-x64   --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o ./publish/win-x64
	cp -r openLuo/data ./publish/win-x64/
	rm -f ./publish/win-x64/config/*.example.json
	cp -r openLuo/data/config/. ./publish/win-x64/config/
	cp -r openLuo/native ./publish/win-x64/

format: format-csharp format-python

format-csharp:
	dotnet format openLuo.sln

format-python:
	@if [ -x ".venv/bin/black" ]; then \
		.venv/bin/black openLuo/data/plugins; \
	elif command -v black >/dev/null 2>&1; then \
		black openLuo/data/plugins; \
	else \
		echo "black 未安装。请先运行 '.venv/bin/python -m pip install black'"; \
		exit 1; \
	fi

clean:
	dotnet clean
	rm -rf ./publish
	rm -rf ./TestResults
	find . -type d -name 'TestResults' -prune -exec rm -rf {} +
	find . -type d -name '__pycache__' -prune -exec rm -rf {} +
	find . -type f -name '*.pyc' -delete
	find . -type f -name '*.pyo' -delete
