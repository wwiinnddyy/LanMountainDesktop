<template>
  <div class="screen">
    <div class="clock">{{ timeText }}</div>
    <div v-if="page === 'home'" class="home">
      <button
        class="openAppsButton touchButton"
        type="button"
        @pointerup="openApps"
        @keydown.enter.prevent="openApps"
        @keydown.space.prevent="openApps"
      >
        应用列表
      </button>
    </div>

    <div v-else class="appsPage">
      <div class="appsTilesArea">
        <div v-if="isLoadingApps" class="appsLoading">加载中...</div>
        <div v-else-if="loadError" class="error">
          <div class="errorHeader">
            <div class="errorTitle">加载失败</div>
            <button class="copyButton touchButton" type="button" @pointerup="copyLoadError">复制错误</button>
          </div>
          <div class="errorBody">{{ loadError }}</div>
        </div>

        <div v-else class="appsList" @scroll="handleAppsListScroll">
          <template v-for="group in groupedApps" :key="group.key">
            <div class="groupHeader">{{ group.label }}</div>
            <button
              v-for="app in group.items"
              :key="app.id"
              class="item touchButton"
              type="button"
              @pointerdown="handleAppPointerDown($event, app.filePath)"
              @pointermove="handleAppPointerMove($event)"
              @pointerup="handleAppPointerUp($event)"
              @pointercancel="handleAppPointerCancel($event)"
              @keydown.enter.prevent="launch(app.filePath)"
              @keydown.space.prevent="launch(app.filePath)"
            >
              <img class="icon" :src="app.iconDataUrl" alt="" />
              <div class="name">{{ app.name }}</div>
            </button>
          </template>
        </div>
      </div>

      <div class="appsBottomBar">
        <button class="backButton touchButton" type="button" @pointerup="closeApps">返回</button>
        <input v-model="query" class="appsSearch" type="text" placeholder="搜索应用..." />
        <button class="appsExitButton touchButton" type="button" @pointerup="handleExit">
          <svg
            class="buttonIcon"
            xmlns="http://www.w3.org/2000/svg"
            width="20"
            height="20"
            viewBox="0 0 20 20"
            aria-hidden="true"
            focusable="false"
          >
            <path
              fill="currentColor"
              d="M8.5 9A1.5 1.5 0 0 0 10 7.5v-4A1.5 1.5 0 0 0 8.5 2h-6A1.5 1.5 0 0 0 1 3.5v4a1.5 1.5 0 0 0 1 1.415l.019.006c.15.051.313.079.481.079zm6.75-3H11V5h4.25A2.75 2.75 0 0 1 18 7.75v6.5A2.75 2.75 0 0 1 15.25 17H4.75A2.75 2.75 0 0 1 2 14.25v-4.3q.243.05.5.05H3v4.25c0 .966.784 1.75 1.75 1.75h10.5A1.75 1.75 0 0 0 17 14.25v-6.5A1.75 1.75 0 0 0 15.25 6M14 12.293l-2.646-2.647a.5.5 0 0 0-.708.708L13.293 13H11.5a.5.5 0 0 0 0 1h3a.5.5 0 0 0 .5-.497V10.5a.5.5 0 0 0-1 0z"
            />
          </svg>
          Windows
        </button>
      </div>
    </div>
    <button v-if="page === 'home'" class="exitButton touchButton" type="button" @pointerup="handleExit">
      <svg
        class="buttonIcon"
        xmlns="http://www.w3.org/2000/svg"
        width="20"
        height="20"
        viewBox="0 0 20 20"
        aria-hidden="true"
        focusable="false"
      >
        <path
          fill="currentColor"
          d="M8.5 9A1.5 1.5 0 0 0 10 7.5v-4A1.5 1.5 0 0 0 8.5 2h-6A1.5 1.5 0 0 0 1 3.5v4a1.5 1.5 0 0 0 1 1.415l.019.006c.15.051.313.079.481.079zm6.75-3H11V5h4.25A2.75 2.75 0 0 1 18 7.75v6.5A2.75 2.75 0 0 1 15.25 17H4.75A2.75 2.75 0 0 1 2 14.25v-4.3q.243.05.5.05H3v4.25c0 .966.784 1.75 1.75 1.75h10.5A1.75 1.75 0 0 0 17 14.25v-6.5A1.75 1.75 0 0 0 15.25 6M14 12.293l-2.646-2.647a.5.5 0 0 0-.708.708L13.293 13H11.5a.5.5 0 0 0 0 1h3a.5.5 0 0 0 .5-.497V10.5a.5.5 0 0 0-1 0z"
        />
      </svg>
      Windows
    </button>
  </div>
</template>

<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'

type Page = 'home' | 'apps'

const now = ref(new Date())
const page = ref<Page>('home')
const apps = ref<Array<{ id: string; name: string; filePath: string; iconDataUrl: string }>>([])
const query = ref('')
const loadError = ref<string | null>(null)
const isLoadingApps = ref(false)
const isAppsScrolling = ref(false)

let timerId: number | undefined
let appsScrollTimerId: number | undefined
let launchTimerId: number | undefined

const activeAppPointer = ref<{
  pointerId: number
  startX: number
  startY: number
  startedAt: number
  moved: boolean
  filePath: string
  element: HTMLElement | null
} | null>(null)

onMounted(() => {
  timerId = window.setInterval(() => {
    now.value = new Date()
  }, 1000)
})

const handleAppsListScroll = (): void => {
  isAppsScrolling.value = true
  if (appsScrollTimerId) window.clearTimeout(appsScrollTimerId)
  appsScrollTimerId = window.setTimeout(() => {
    isAppsScrolling.value = false
  }, 140)
}

const loadApps = async (): Promise<void> => {
  if (isLoadingApps.value) return
  isLoadingApps.value = true
  loadError.value = null
  try {
    const result = await window.api.call<{
      apps: Array<{ id: string; name: string; filePath: string; iconDataUrl: string }>
      error: string | null
    }>({
      method: 'GET',
      path: '/apps/list'
    })
    apps.value = result.apps ?? []
    loadError.value = result.error ?? null
  } catch (error) {
    const message = error instanceof Error ? error.message : 'UnknownError'
    apps.value = []
    loadError.value = message
  } finally {
    isLoadingApps.value = false
  }
}

const openApps = async (): Promise<void> => {
  page.value = 'apps'
  if (apps.value.length === 0 || loadError.value) {
    await loadApps()
  }
}

const closeApps = (): void => {
  page.value = 'home'
  query.value = ''
}

onBeforeUnmount(() => {
  if (timerId) window.clearInterval(timerId)
  if (appsScrollTimerId) window.clearTimeout(appsScrollTimerId)
  if (launchTimerId) window.clearTimeout(launchTimerId)
})

const timeText = computed(() => {
  return new Intl.DateTimeFormat('zh-CN', {
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit'
  }).format(now.value)
})

const filteredApps = computed(() => {
  const q = query.value.trim().toLowerCase()
  if (!q) return apps.value
  return apps.value.filter((a) => a.name.toLowerCase().includes(q))
})

const collator =
  Intl.Collator.supportedLocalesOf(['zh-Hans-CN-u-co-pinyin']).length > 0
    ? new Intl.Collator(['zh-Hans-CN-u-co-pinyin', 'en'], {
        numeric: true,
        sensitivity: 'base'
      })
    : new Intl.Collator(['zh-CN', 'en'], { numeric: true, sensitivity: 'base' })

const pinyinInitialBoundaries: Array<{ letter: string; char: string }> = [
  { letter: 'A', char: '阿' },
  { letter: 'B', char: '八' },
  { letter: 'C', char: '嚓' },
  { letter: 'D', char: '搭' },
  { letter: 'E', char: '蛾' },
  { letter: 'F', char: '发' },
  { letter: 'G', char: '噶' },
  { letter: 'H', char: '哈' },
  { letter: 'J', char: '击' },
  { letter: 'K', char: '喀' },
  { letter: 'L', char: '垃' },
  { letter: 'M', char: '妈' },
  { letter: 'N', char: '拿' },
  { letter: 'O', char: '哦' },
  { letter: 'P', char: '啪' },
  { letter: 'Q', char: '期' },
  { letter: 'R', char: '然' },
  { letter: 'S', char: '撒' },
  { letter: 'T', char: '塌' },
  { letter: 'W', char: '挖' },
  { letter: 'X', char: '昔' },
  { letter: 'Y', char: '压' },
  { letter: 'Z', char: '匝' }
]

const groupKeyForName = (name: string): string => {
  const first = name.trim().charAt(0)
  if (!first) return '#'
  const upper = first.toUpperCase()
  if (upper >= 'A' && upper <= 'Z') return upper
  if (upper >= '0' && upper <= '9') return '#'
  if (/[\u3400-\u9fff]/.test(first)) {
    for (let i = 0; i < pinyinInitialBoundaries.length; i += 1) {
      const current = pinyinInitialBoundaries[i]
      const next = pinyinInitialBoundaries[i + 1]
      if (!next) return current.letter
      if (collator.compare(first, next.char) < 0) return current.letter
    }
  }

  return '#'
}

const groupedApps = computed(() => {
  const sorted = [...filteredApps.value].sort((a, b) => collator.compare(a.name, b.name))
  const groups: Array<{
    key: string
    label: string
    items: Array<{ id: string; name: string; filePath: string; iconDataUrl: string }>
  }> = []

  for (const app of sorted) {
    const key = groupKeyForName(app.name)
    const last = groups[groups.length - 1]
    if (!last || last.key !== key) {
      groups.push({ key, label: key, items: [app] })
    } else {
      last.items.push(app)
    }
  }

  const alpha = groups.filter((g) => g.key !== '#')
  const other = groups.filter((g) => g.key === '#')
  return [...alpha, ...other]
})

const launch = async (filePath: string): Promise<void> => {
  await window.api.call({ method: 'POST', path: '/apps/launch', body: { filePath } })
}

const handleAppPointerDown = (event: PointerEvent, filePath: string): void => {
  if (launchTimerId) window.clearTimeout(launchTimerId)
  launchTimerId = undefined

  const element = event.currentTarget instanceof HTMLElement ? event.currentTarget : null
  activeAppPointer.value = {
    pointerId: event.pointerId,
    startX: event.clientX,
    startY: event.clientY,
    startedAt: performance.now(),
    moved: false,
    filePath,
    element
  }

  try {
    element?.setPointerCapture(event.pointerId)
  } catch {}
}

const handleAppPointerMove = (event: PointerEvent): void => {
  const state = activeAppPointer.value
  if (!state || state.pointerId !== event.pointerId) return
  if (state.moved) return

  const dx = Math.abs(event.clientX - state.startX)
  const dy = Math.abs(event.clientY - state.startY)
  if (dx >= 12 || dy >= 12) state.moved = true
}

const handleAppPointerCancel = (event: PointerEvent): void => {
  const state = activeAppPointer.value
  if (!state || state.pointerId !== event.pointerId) return
  activeAppPointer.value = null
}

const handleAppPointerUp = (event: PointerEvent): void => {
  const state = activeAppPointer.value
  if (!state || state.pointerId !== event.pointerId) return
  activeAppPointer.value = null

  try {
    state.element?.releasePointerCapture(event.pointerId)
  } catch {}

  const elapsed = performance.now() - state.startedAt
  if (state.moved) return
  if (isAppsScrolling.value) return
  if (elapsed > 900) return

  const launchDelayMs = 180
  launchTimerId = window.setTimeout(async () => {
    if (isAppsScrolling.value) return
    await launch(state.filePath)
  }, launchDelayMs)
}

const handleExit = async (): Promise<void> => {
  await window.api.call({ method: 'POST', path: '/app/minimize' })
}

const copyLoadError = async (): Promise<void> => {
  if (!loadError.value) return
  const text = loadError.value
  try {
    await navigator.clipboard.writeText(text)
  } catch {
    const textarea = document.createElement('textarea')
    textarea.value = text
    textarea.style.position = 'fixed'
    textarea.style.left = '-1000px'
    textarea.style.top = '-1000px'
    document.body.appendChild(textarea)
    textarea.focus()
    textarea.select()
    document.execCommand('copy')
    document.body.removeChild(textarea)
  }
}
</script>
