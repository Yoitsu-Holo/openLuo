# 扩展项目（目录:程序集名，注意 Linux 大小写敏感）
EXT_PAIRS = memory:Memory companion:Companion world:World party:Party

.PHONY: run run-playground test test-kernel test-e2e build publish publish-fast format format-csharp format-python clean

run:
	dotnet run --project openLuo

run-playground:
	dotnet run --project openLuo.playgraound

test:
	dotnet test openLuo.slnx

test-kernel:
	dotnet test tests/openLuo.Capabilities.Tests && dotnet test tests/openLuo.AgentContext.Tests

test-e2e:
	dotnet test tests/openLuo.E2E.Tests

build:
	dotnet build openLuo.slnx -c Release

# 扩展 DLL 增量构建：源码/csproj/配置比 DLL 新才编译（make 依赖判断，无改动 0s）
define ext-rule
extensions/$(word 1,$(subst :, ,$(1)))/bin/Release/net10.0/openLuo.Extension.$(word 2,$(subst :, ,$(1))).dll: \
		extensions/$(word 1,$(subst :, ,$(1)))/*.cs \
		extensions/$(word 1,$(subst :, ,$(1)))/*.csproj \
		extensions/$(word 1,$(subst :, ,$(1)))/extension.jsonc
	dotnet build extensions/$(word 1,$(subst :, ,$(1))) -c Release --nologo -v q --no-restore \
		2>/dev/null || dotnet build extensions/$(word 1,$(subst :, ,$(1))) -c Release --nologo -v q
endef
$(foreach p,$(EXT_PAIRS),$(eval $(call ext-rule,$(p))))

EXT_DLLS = $(foreach p,$(EXT_PAIRS),extensions/$(word 1,$(subst :, ,$(p)))/bin/Release/net10.0/openLuo.Extension.$(word 2,$(subst :, ,$(p))).dll)

# 发布时保留的不可再生内容（白名单：其余内容清空重建；在此增删条目）
# build.sh: 用户自定义构建脚本；config/: 生产密钥/配置；
# game.db*: 数据库本体 + 事务残留文件（-journal 回滚日志 / -wal/-shm WAL 伴生文件）。
#   进程非正常停止时未合并事务在残留文件里，删除即丢已提交数据。
KEEP_ENTRIES = build.sh config game.db game.db-journal game.db-wal game.db-shm
KEEP_FIND = $(foreach e,$(KEEP_ENTRIES),! -path './publish/linux-x64/$(e)' ! -path './publish/linux-x64/$(e)/*')

# 组装发布目录（publish 产物 + data/native/mcp + 扩展），保留 KEEP_ENTRIES
# 只清空 linux-x64 的内容，不删除目录本身（保留 inode，避免符号链接/
# 工作目录/监控失效）。保留项由 find 白名单排除，无需挪动。
define assemble-publish
	@rm -rf ./publish/.config-staging ./publish/linux-x64.bak
	@mkdir -p ./publish/linux-x64
	@find ./publish/linux-x64 -mindepth 1 $(KEEP_FIND) -delete
	cp -r /tmp/openluo-pub/. ./publish/linux-x64/
	cp -r openLuo/data ./publish/linux-x64/
	cp -r openLuo/native ./publish/linux-x64/
	cp -r mcp ./publish/linux-x64/
	@for ext in memory companion world party; do \
		mkdir -p ./publish/linux-x64/extensions/$$ext; \
		cp extensions/$$ext/bin/Release/net10.0/openLuo.Extension.*.dll ./publish/linux-x64/extensions/$$ext/; \
		cp extensions/$$ext/extension.jsonc ./publish/linux-x64/extensions/$$ext/; \
	done
endef

# 生产发布：单文件（native 库独立放置，启动免自解压）。publish 到临时空目录
# （最快路径），再组装进发布目录——避免对旧产物逐文件对比删除。
publish: $(EXT_DLLS)
	rm -rf /tmp/openluo-pub
	dotnet publish openLuo -c Release -r linux-x64 --self-contained true \
		-p:PublishSingleFile=true \
		-o /tmp/openluo-pub --nologo -tl
	$(assemble-publish)

# 快速迭代发布：目录形态（无单文件打包），验证逻辑用；生产部署仍用 publish
publish-fast: $(EXT_DLLS)
	rm -rf /tmp/openluo-pub
	dotnet publish openLuo -c Release -r linux-x64 --self-contained true \
		-o /tmp/openluo-pub --nologo -tl
	$(assemble-publish)

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
	dotnet clean openLuo.slnx
	@if [ -d ./publish/linux-x64 ]; then find ./publish/linux-x64 -mindepth 1 $(KEEP_FIND) -delete; fi
	rm -rf ./TestResults
