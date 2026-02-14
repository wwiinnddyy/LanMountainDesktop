<template>
  <div class="screen">
    <div class="clock">{{ timeText }}</div>
    <div
      class="pagesViewport"
      @pointerdown="handlePagePointerDown"
      @pointermove="handlePagePointerMove"
      @pointerup="handlePagePointerUp"
      @pointercancel="handlePagePointerCancel"
    >
      <div class="pagesTrack" :data-active="mainPage">
        <div class="page page--home">
          <div class="home">
            <div class="desktopGridShell">
              <div class="desktopGrid">
                <component
                  :is="tile.clickable ? 'button' : 'div'"
                  v-for="tile in placedDesktopTiles"
                  :key="tile.id"
                  class="deskTile touchButton"
                  :class="[
                    tile.variant ? `deskTile--${tile.variant}` : '',
                    (tile.id === 'writingPanel' || tile.id === 'notesPanel') ? 'deskTile--panel' : '',
                    tile.id === 'notesPanel' ? 'deskTile--notes' : ''
                  ]"
                  :style="{
                    gridColumn: `${tile.col} / span ${tile.colSpan}`,
                    gridRow: `${tile.row} / span ${tile.rowSpan}`
                  }"
                  :type="tile.clickable ? 'button' : undefined"
                  @pointerup="tile.clickable ? handleTilePointerUp(tile) : undefined"
                >
                  <template v-if="tile.id === 'writingPanel'">
                    <div class="writingPanelTitle">书写</div>
                    <div class="writingPanelButtons">
                      <button
                        v-for="a in writingActions"
                        :key="a.id"
                        class="writingActionButton touchButton"
                        type="button"
                        @pointerup="handleWritingActionPointerUp(a.id)"
                      >
                        <svg
                          class="writingActionIcon"
                          xmlns="http://www.w3.org/2000/svg"
                          width="18"
                          height="18"
                          viewBox="0 0 20 20"
                          aria-hidden="true"
                          focusable="false"
                        >
                          <path fill="currentColor" :d="iconPath(a.icon)" />
                        </svg>
                        <div class="writingActionText">{{ a.label }}</div>
                      </button>
                    </div>
                  </template>
                  <template v-else-if="tile.id === 'notesPanel'">
                    <div class="notesWidget">
                      <div class="notesHeader">
                        <div class="notesHeaderTitle">作业版</div>
                        <button class="notesAddButton touchButton" type="button" @pointerup="addNote">
                          新建
                        </button>
                      </div>
                      <div class="notesGrid">
                        <div v-for="note in notes" :key="note.id" class="noteCard">
                          <textarea v-model="note.text" class="noteTextarea" placeholder="写点什么..." />
                        </div>
                      </div>
                    </div>
                  </template>
                  <template v-else>
                    <div class="deskTileRow">
                      <svg
                        class="deskTileIcon"
                        xmlns="http://www.w3.org/2000/svg"
                        width="28"
                        height="28"
                        viewBox="0 0 20 20"
                        aria-hidden="true"
                        focusable="false"
                      >
                        <path fill="currentColor" :d="iconPath(tile.icon)" />
                      </svg>
                      <div class="deskTileText">{{ tile.title }}</div>
                    </div>
                  </template>
                </component>
              </div>
            </div>
          </div>
        </div>

        <div class="page page--apps">
          <div class="appsPage">
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
                回到Window
              </button>
            </div>
          </div>
        </div>
      </div>
    </div>

    <div v-if="isSettingsOpen" class="settingsPage">
      <div class="settingsContentArea">
        <div class="settingsSidebar">
          <div class="settingsSidebarTitle">设置</div>
          <button
            class="settingsTabButton touchButton"
            type="button"
            :data-active="settingsTab === 'homeEdit'"
            @pointerup="selectSettingsTab('homeEdit')"
          >
            主页编辑
          </button>
        </div>

        <div class="settingsDetail">
          <div class="settingsDetailHeader">
            <div class="settingsDetailTitle">主页编辑</div>
            <div class="settingsDetailHeaderActions">
              <button
                v-if="!isHomeEditMode"
                class="settingsPrimaryButton touchButton"
                type="button"
                @pointerup="enterHomeEdit"
              >
                进入主页编辑
              </button>
              <button
                v-else
                class="settingsPrimaryButton touchButton"
                type="button"
                @pointerup="exitHomeEdit"
              >
                退出编辑
              </button>
              <button class="settingsResetButton touchButton" type="button" @pointerup="restoreDefaultWidgets">
                恢复默认
              </button>
            </div>
          </div>

          <div class="settingsDetailBody">
            <template v-if="!isHomeEditMode">
              <div class="settingsSection">
                <div class="settingsSectionTitle">组件与点击行为</div>
                <div class="settingsPreviewList">
                  <div v-for="w in settingsWidgets" :key="w.id" class="settingsPreviewItem">
                    <div class="settingsPreviewMain">
                      <div class="settingsPreviewTitle">{{ w.title }}</div>
                      <div class="settingsPreviewMeta">
                        {{ w.sizeLabel }} · {{ w.enabled ? '已添加' : '未添加' }} · {{ w.visible ? '显示中' : '未显示' }}
                      </div>
                    </div>
                    <div class="settingsPreviewBehavior">
                      <template v-if="w.id === 'writingPanel'">
                        <div class="settingsPreviewBehaviorTitle">内置按钮</div>
                        <div v-for="a in writingActions" :key="a.id" class="settingsPreviewBehaviorRow">
                          <div class="settingsPreviewBehaviorLabel">{{ a.label }}</div>
                          <div class="settingsPreviewBehaviorValue">{{ actionSummary(a.id) }}</div>
                        </div>
                      </template>
                      <template v-else>
                        <div class="settingsPreviewBehaviorTitle">点击行为</div>
                        <div class="settingsPreviewBehaviorValue">{{ actionSummary(w.id) }}</div>
                      </template>
                    </div>
                  </div>
                </div>
              </div>
            </template>

            <template v-else>
              <div class="settingsSection">
                <div class="settingsSectionTitle">主页组件（点按添加/移除）</div>
                <div class="homeEditGrid">
                  <button
                    v-for="w in widgetsInOrder"
                    :key="w.id"
                    class="homeWidgetCard touchButton"
                    type="button"
                    :data-active="widgetsEnabled[w.id] !== false"
                    :data-selected="homeEditSelectedId === w.id"
                    @pointerup="toggleHomeWidget(w.id)"
                  >
                    <svg
                      class="homeWidgetCardIcon"
                      xmlns="http://www.w3.org/2000/svg"
                      width="22"
                      height="22"
                      viewBox="0 0 20 20"
                      aria-hidden="true"
                      focusable="false"
                    >
                      <path fill="currentColor" :d="iconPath(w.icon)" />
                    </svg>
                    <div class="homeWidgetCardTitle">{{ w.title }}</div>
                    <div class="homeWidgetCardMeta">{{ w.size }}</div>
                  </button>
                </div>
              </div>

              <div class="settingsSection">
                <div class="settingsSectionTitle">排序（仅已添加）</div>
                <div class="homeEditOrderList">
                  <div v-for="id in enabledWidgetOrder" :key="id" class="homeEditOrderRow">
                    <div class="homeEditOrderTitle">{{ widgetTitle(id) }}</div>
                    <div class="homeEditOrderActions">
                      <button class="settingsActionButton touchButton" type="button" @pointerup="moveEnabledWidget(id, -1)">
                        上移
                      </button>
                      <button class="settingsActionButton touchButton" type="button" @pointerup="moveEnabledWidget(id, 1)">
                        下移
                      </button>
                      <button class="settingsActionButton touchButton" type="button" @pointerup="selectHomeWidget(id)">
                        设置
                      </button>
                    </div>
                  </div>
                </div>
              </div>

              <div class="settingsSection">
                <div class="settingsSectionTitle">详细设置：{{ widgetTitle(homeEditSelectedId) }}</div>
                <div class="settingsConfigArea">
                  <template v-if="homeEditSelectedId === 'writingPanel' || homeEditSelectedId === 'notesPanel'">
                    <div v-if="homeEditSelectedId === 'writingPanel'" v-for="a in writingActions" :key="a.id" class="settingsSubAction">
                      <div class="settingsSubActionTitle">{{ a.label }}</div>
                      <select
                        class="settingsSelect touchButton"
                        :value="actionConfigs[a.id]?.kind ?? 'url'"
                        @change="setActionKind(a.id, ($event.target as HTMLSelectElement).value as ActionKind)"
                      >
                        <option value="app">打开应用</option>
                        <option value="url">打开URL</option>
                      </select>
                      <input
                        class="settingsInput"
                        type="text"
                        :value="actionConfigs[a.id]?.target ?? ''"
                        @input="setActionTarget(a.id, ($event.target as HTMLInputElement).value)"
                        placeholder="输入开始菜单路径或URL"
                      />
                      <button class="settingsMiniButton touchButton" type="button" @pointerup="clearAction(a.id)">
                        清空
                      </button>
                    </div>
                    <div v-else class="settingsSingleActionRow">
                      <div class="settingsActionHint">作业版目前不支持自定义点击行为。</div>
                    </div>
                  </template>
                  <template v-else>
                    <div class="settingsSingleActionRow">
                      <select
                        class="settingsSelect touchButton"
                        :value="actionConfigs[homeEditSelectedId]?.kind ?? 'url'"
                        @change="
                          setActionKind(homeEditSelectedId, ($event.target as HTMLSelectElement).value as ActionKind)
                        "
                      >
                        <option value="app">打开应用</option>
                        <option value="url">打开URL</option>
                      </select>
                      <input
                        class="settingsInput"
                        type="text"
                        :value="actionConfigs[homeEditSelectedId]?.target ?? ''"
                        @input="setActionTarget(homeEditSelectedId, ($event.target as HTMLInputElement).value)"
                        placeholder="输入开始菜单路径或URL"
                      />
                      <button class="settingsMiniButton touchButton" type="button" @pointerup="clearAction(homeEditSelectedId)">
                        清空
                      </button>
                    </div>
                    <div class="settingsActionHint">当前：{{ actionSummary(homeEditSelectedId) }}</div>
                  </template>
                </div>
              </div>
            </template>
          </div>
        </div>
      </div>
      <div class="settingsBottomBar">
        <button class="backButton touchButton" type="button" @pointerup="closeSettings">返回</button>
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
          回到Window
        </button>
      </div>
    </div>
    <button v-if="mainPage === 'home' && !isSettingsOpen" class="exitButton touchButton" type="button" @pointerup="handleExit">
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
      回到Window
    </button>
    <button
      v-if="mainPage === 'home' && !isSettingsOpen"
      class="settingsButton touchButton"
      type="button"
      @pointerup="openSettings"
    >
      设置
    </button>
  </div>
</template>

<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'

type MainPage = 'home' | 'apps'

const now = ref(new Date())
const mainPage = ref<MainPage>('home')
const isSettingsOpen = ref(false)
const apps = ref<Array<{ id: string; name: string; filePath: string; iconDataUrl: string }>>([])
const query = ref('')
const loadError = ref<string | null>(null)
const isLoadingApps = ref(false)
const isAppsScrolling = ref(false)
type Note = { id: string; text: string }
const notes = ref<Note[]>([])

type ActionKind = 'app' | 'url'
type ActionConfig = { kind: ActionKind; target: string }
type WidgetSize = '1x1' | '2x1' | '1x2' | '2x2' | '4x2' | '2x4' | '4x4' | '8x4'
type TileIcon = 'pen' | 'folder' | 'camera' | 'globe' | 'apps' | 'note' | 'doc' | 'comment'
type DesktopTile = {
  id: string
  title: string
  size: WidgetSize
  icon: TileIcon
  clickable: boolean
  onActivate?: () => void
  variant?: 'accent' | 'soft'
}

type PlacedDesktopTile = DesktopTile & {
  row: number
  col: number
  rowSpan: number
  colSpan: number
}

const iconPath = (icon: TileIcon): string => {
  switch (icon) {
    case 'pen':
      return 'M14.1 2.65a2.25 2.25 0 0 1 3.182 3.182l-8.6 8.6a2.25 2.25 0 0 1-1.04.576l-3.1.775a.75.75 0 0 1-.91-.91l.775-3.1a2.25 2.25 0 0 1 .576-1.04zm2.121 1.06a.75.75 0 0 0-1.06 0L6.7 12.174a.75.75 0 0 0-.192.346l-.44 1.76 1.76-.44a.75.75 0 0 0 .346-.192l8.462-8.462a.75.75 0 0 0 0-1.06z'
    case 'folder':
      return 'M3.5 4.5A2.5 2.5 0 0 1 6 2h2.25c.43 0 .84.184 1.126.505L10.2 3.5H14A2.5 2.5 0 0 1 16.5 6v7.5A2.5 2.5 0 0 1 14 16H6A2.5 2.5 0 0 1 3.5 13.5zM6 3.5A1 1 0 0 0 5 4.5V5h10v-1a1 1 0 0 0-1-1h-4.1a.75.75 0 0 1-.562-.253L8.086 3.5zm9 3H5v7a1 1 0 0 0 1 1h8a1 1 0 0 0 1-1z'
    case 'camera':
      return 'M7 4.5h6l.6 1.2c.127.254.387.414.67.414H15A2.5 2.5 0 0 1 17.5 8.6v6.9A2.5 2.5 0 0 1 15 18H5A2.5 2.5 0 0 1 2.5 15.5V8.6A2.5 2.5 0 0 1 5 6.114h.73c.283 0 .543-.16.67-.414zM10 15.5a3 3 0 1 0 0-6 3 3 0 0 0 0 6z'
    case 'globe':
      return 'M10 2a8 8 0 1 1 0 16 8 8 0 0 1 0-16m5.93 7H13.6a12 12 0 0 0-1.03-4.09A6.5 6.5 0 0 1 15.93 9M10 3.5c-.9 0-1.98 1.6-2.45 5.5h4.9c-.47-3.9-1.55-5.5-2.45-5.5m-2.57 1.41A12 12 0 0 0 6.4 9H4.07a6.5 6.5 0 0 1 3.36-4.09M4.07 11H6.4c.17 1.5.56 2.92 1.03 4.09A6.5 6.5 0 0 1 4.07 11m3.48 0c.47 3.9 1.55 5.5 2.45 5.5s1.98-1.6 2.45-5.5zm5.02 4.09c.47-1.17.86-2.59 1.03-4.09h2.33a6.5 6.5 0 0 1-3.36 4.09'
    case 'apps':
      return 'M4.5 3A1.5 1.5 0 0 0 3 4.5v2A1.5 1.5 0 0 0 4.5 8h2A1.5 1.5 0 0 0 8 6.5v-2A1.5 1.5 0 0 0 6.5 3zm9 0A1.5 1.5 0 0 0 12 4.5v2A1.5 1.5 0 0 0 13.5 8h2A1.5 1.5 0 0 0 17 6.5v-2A1.5 1.5 0 0 0 15.5 3zM3 13.5A1.5 1.5 0 0 1 4.5 12h2A1.5 1.5 0 0 1 8 13.5v2A1.5 1.5 0 0 1 6.5 17h-2A1.5 1.5 0 0 1 3 15.5zm10.5-1.5A1.5 1.5 0 0 0 12 13.5v2a1.5 1.5 0 0 0 1.5 1.5h2a1.5 1.5 0 0 0 1.5-1.5v-2a1.5 1.5 0 0 0-1.5-1.5z'
    case 'note':
      return 'M6 2.5A2.5 2.5 0 0 0 3.5 5v10A2.5 2.5 0 0 0 6 17.5h5.5a.75.75 0 0 0 .53-.22l3.25-3.25a.75.75 0 0 0 .22-.53V5A2.5 2.5 0 0 0 13 2.5zm6.5 12.94V14a.5.5 0 0 1 .5-.5h1.44z'
    case 'doc':
      return 'M6 2.5A2.5 2.5 0 0 0 3.5 5v10A2.5 2.5 0 0 0 6 17.5h8A2.5 2.5 0 0 0 16.5 15V7.75a.75.75 0 0 0-.22-.53l-3.5-3.5a.75.75 0 0 0-.53-.22zM12.5 4.56 14.44 6.5H13a.5.5 0 0 1-.5-.5z'
    case 'comment':
      return 'M5.5 3.5A2.5 2.5 0 0 0 3 6v6a2.5 2.5 0 0 0 2.5 2.5H7.3l2.2 1.76a.75.75 0 0 0 .94 0l2.2-1.76H14.5A2.5 2.5 0 0 0 17 12V6a2.5 2.5 0 0 0-2.5-2.5z'
  }
}

const writingActions: Array<{ id: string; label: string; icon: TileIcon }> = [
  { id: 'writing.whiteboard', label: '白板书写', icon: 'pen' },
  { id: 'writing.annotate', label: '智能批注', icon: 'comment' },
  { id: 'writing.historyNotes', label: '历史笔记', icon: 'note' },
  { id: 'writing.classNotes', label: '课堂笔记', icon: 'note' },
  { id: 'writing.historyDocs', label: '历史文档', icon: 'doc' }
]

const actionConfigs = ref<Record<string, ActionConfig>>({})
let actionSaveTimerId: number | undefined

type SettingsTab = 'homeEdit'
const settingsTab = ref<SettingsTab>('homeEdit')
const isHomeEditMode = ref(false)
const homeEditSelectedId = ref<string>('whiteboard')

let pageSwipePointerId: number | null = null
let pageSwipeStartX = 0
let pageSwipeStartY = 0
let pageSwipeStartTime = 0
let pageSwipeStartMainPage: MainPage = 'home'
let suppressTapUntil = 0

const availableWidgets: DesktopTile[] = [
  { id: 'writingPanel', title: '书写', size: '2x4', icon: 'pen', clickable: false, variant: 'accent' },
  { id: 'notesPanel', title: '作业版', size: '4x4', icon: 'note', clickable: false, variant: 'soft' },
  {
    id: 'whiteboard',
    title: '白板',
    size: '2x2',
    icon: 'doc',
    clickable: true,
    onActivate: () => void activateAction('whiteboard'),
    variant: 'soft'
  },
  {
    id: 'fileManager',
    title: '文件',
    size: '2x2',
    icon: 'folder',
    clickable: true,
    onActivate: () => void activateAction('fileManager')
  },
  {
    id: 'visualPresenter',
    title: '展台',
    size: '2x2',
    icon: 'camera',
    clickable: true,
    onActivate: () => void activateAction('visualPresenter')
  },
  {
    id: 'browser',
    title: '浏览器',
    size: '2x2',
    icon: 'globe',
    clickable: true,
    onActivate: () => void activateAction('browser')
  },
  {
    id: 'moreApps',
    title: '全部应用',
    size: '2x2',
    icon: 'apps',
    clickable: true,
    onActivate: () => void activateAction('moreApps', () => void openApps()),
    variant: 'accent'
  },
  {
    id: 'calculator',
    title: '计算器',
    size: '1x1',
    icon: 'apps',
    clickable: true,
    onActivate: () => void activateAction('calculator')
  },
  {
    id: 'calendar',
    title: '日历',
    size: '2x1',
    icon: 'apps',
    clickable: true,
    onActivate: () => void activateAction('calendar')
  },
  {
    id: 'timer',
    title: '计时器',
    size: '1x1',
    icon: 'apps',
    clickable: true,
    onActivate: () => void activateAction('timer')
  },
  {
    id: 'camera',
    title: '相机',
    size: '1x1',
    icon: 'camera',
    clickable: true,
    onActivate: () => void activateAction('camera')
  }
]

const widgetsOrder = ref<string[]>([
  'writingPanel',
  'notesPanel',
  'whiteboard',
  'fileManager',
  'visualPresenter',
  'browser',
  'moreApps'
])
const widgetsEnabled = ref<Record<string, boolean>>({
  writingPanel: true,
  notesPanel: true,
  whiteboard: true,
  fileManager: true,
  visualPresenter: true,
  browser: true,
  moreApps: true
})

let widgetsSaveTimerId: number | undefined

const widgetsInOrder = computed<DesktopTile[]>(() => {
  const map = new Map(availableWidgets.map((w) => [w.id, w]))
  const seen = new Set<string>()
  const ordered: DesktopTile[] = []
  for (const id of widgetsOrder.value) {
    const w = map.get(id)
    if (!w) continue
    if (seen.has(id)) continue
    seen.add(id)
    ordered.push(w)
  }
  for (const w of availableWidgets) {
    if (seen.has(w.id)) continue
    ordered.push(w)
  }
  return ordered
})

const enabledWidgets = computed<DesktopTile[]>(() => {
  return widgetsInOrder.value.filter((w) => widgetsEnabled.value[w.id] !== false)
})

const placedDesktopTiles = computed<PlacedDesktopTile[]>(() => {
  const cols = 8
  const rows = 5
  const occupied = Array.from({ length: rows }, () => Array.from({ length: cols }, () => false))

  const tryPlace = (tile: DesktopTile): PlacedDesktopTile | null => {
    let rowSpan = 1
    let colSpan = 1
    if (tile.size === '8x4') {
      rowSpan = 4
      colSpan = 8
    } else if (tile.size === '4x4') {
      rowSpan = 4
      colSpan = 4
    } else if (tile.size === '2x4') {
      rowSpan = 4
      colSpan = 2
    } else if (tile.size === '4x2') {
      rowSpan = 2
      colSpan = 4
    } else if (tile.size === '2x2') {
      rowSpan = 2
      colSpan = 2
    } else if (tile.size === '2x1') {
      rowSpan = 1
      colSpan = 2
    } else if (tile.size === '1x2') {
      rowSpan = 2
      colSpan = 1
    }

    for (let r = 1; r <= rows - rowSpan + 1; r += 1) {
      for (let c = 1; c <= cols - colSpan + 1; c += 1) {
        let ok = true
        for (let rr = r; rr < r + rowSpan; rr += 1) {
          for (let cc = c; cc < c + colSpan; cc += 1) {
            if (occupied[rr - 1]?.[cc - 1]) {
              ok = false
              break
            }
          }
          if (!ok) break
        }
        if (!ok) continue
        for (let rr = r; rr < r + rowSpan; rr += 1) {
          for (let cc = c; cc < c + colSpan; cc += 1) {
            occupied[rr - 1][cc - 1] = true
          }
        }
        return { ...tile, row: r, col: c, rowSpan, colSpan }
      }
    }
    return null
  }

  const out: PlacedDesktopTile[] = []
  for (const tile of enabledWidgets.value) {
    const placed = tryPlace(tile)
    if (placed) out.push(placed)
  }
  return out
})

const placedWidgetIds = computed(() => new Set(placedDesktopTiles.value.map((t) => t.id)))

const settingsWidgets = computed(() => {
  return widgetsInOrder.value.map((w) => {
    const sizeLabel = w.size
    const enabled = widgetsEnabled.value[w.id] !== false
    const visible = placedWidgetIds.value.has(w.id)
    return { id: w.id, title: w.title, sizeLabel, enabled, visible }
  })
})

const widgetTitle = (id: string): string => {
  if (id === 'writingPanel') return '书写'
  const found = availableWidgets.find((w) => w.id === id)
  if (found) return found.title
  const foundAction = writingActions.find((a) => a.id === id)
  if (foundAction) return foundAction.label
  return id
}

const actionSummary = (id: string): string => {
  const config = actionConfigs.value[id]
  const target = config?.target?.trim() ?? ''
  if (!config || !target) {
    if (id === 'moreApps') return '默认：打开应用列表'
    return '未配置'
  }
  return config.kind === 'app' ? `打开应用：${target}` : `打开URL：${target}`
}

const enabledWidgetOrder = computed(() => {
  return widgetsOrder.value.filter((id) => widgetsEnabled.value[id] !== false)
})

const moveEnabledWidget = (id: string, delta: -1 | 1): void => {
  const enabled = enabledWidgetOrder.value
  const index = enabled.indexOf(id)
  if (index < 0) return
  const nextIndex = index + delta
  if (nextIndex < 0 || nextIndex >= enabled.length) return
  const otherId = enabled[nextIndex]

  const full = [...widgetsOrder.value]
  const a = full.indexOf(id)
  const b = full.indexOf(otherId)
  if (a < 0 || b < 0) return
  full[a] = otherId
  full[b] = id
  widgetsOrder.value = full
}

const selectSettingsTab = (tab: SettingsTab): void => {
  settingsTab.value = tab
  isHomeEditMode.value = false
}

const enterHomeEdit = (): void => {
  settingsTab.value = 'homeEdit'
  isHomeEditMode.value = true
  if (!homeEditSelectedId.value) {
    homeEditSelectedId.value = 'whiteboard'
  }
}

const exitHomeEdit = (): void => {
  isHomeEditMode.value = false
}

const selectHomeWidget = (id: string): void => {
  homeEditSelectedId.value = id
}

const toggleHomeWidget = (id: string): void => {
  homeEditSelectedId.value = id
  toggleWidgetEnabled(id)
}

let timerId: number | undefined
let appsScrollTimerId: number | undefined
let launchTimerId: number | undefined
let notesSaveTimerId: number | undefined

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

  try {
    const savedV2 = window.localStorage.getItem('lanmountain.notes.v2')
    if (typeof savedV2 === 'string' && savedV2) {
      const parsed = JSON.parse(savedV2) as { notes?: unknown }
      const rawNotes = Array.isArray(parsed.notes) ? parsed.notes : []
      const loaded: Note[] = rawNotes
        .map((n) => {
          const id = typeof (n as { id?: unknown }).id === 'string' ? (n as { id: string }).id : ''
          const text = typeof (n as { text?: unknown }).text === 'string' ? (n as { text: string }).text : ''
          if (!id) return null
          return { id, text }
        })
        .filter(Boolean) as Note[]
      notes.value = loaded
    } else {
      const savedV1 = window.localStorage.getItem('lanmountain.notes.v1')
      if (typeof savedV1 === 'string' && savedV1.trim()) {
        notes.value = [{ id: `${Date.now()}-${Math.random().toString(16).slice(2)}`, text: savedV1 }]
        window.localStorage.removeItem('lanmountain.notes.v1')
      } else {
        notes.value = []
      }
    }
  } catch {}

  try {
    const raw = window.localStorage.getItem('lanmountain.desktop.widgets.v1')
    if (typeof raw !== 'string' || !raw) return
    const parsed = JSON.parse(raw) as {
      order?: unknown
      enabled?: unknown
    }
    const order = Array.isArray(parsed.order) ? parsed.order.filter((x) => typeof x === 'string') : null
    const enabled =
      parsed.enabled && typeof parsed.enabled === 'object' && !Array.isArray(parsed.enabled) ? parsed.enabled : null
    if (order) widgetsOrder.value = order as string[]
    if (enabled) {
      const next: Record<string, boolean> = { ...widgetsEnabled.value }
      for (const [k, v] of Object.entries(enabled as Record<string, unknown>)) {
        if (typeof v === 'boolean') next[k] = v
      }
      widgetsEnabled.value = next
    }
  } catch {}

  try {
    const raw = window.localStorage.getItem('lanmountain.desktop.actions.v1')
    if (typeof raw === 'string' && raw) {
      const parsed = JSON.parse(raw) as { actions?: unknown }
      const actions =
        parsed.actions && typeof parsed.actions === 'object' && !Array.isArray(parsed.actions) ? parsed.actions : null
      if (actions) {
        const next: Record<string, ActionConfig> = {}
        for (const [k, v] of Object.entries(actions as Record<string, unknown>)) {
          if (!v || typeof v !== 'object' || Array.isArray(v)) continue
          const kind = (v as { kind?: unknown }).kind
          const target = (v as { target?: unknown }).target
          if ((kind === 'app' || kind === 'url') && typeof target === 'string') {
            next[k] = { kind, target }
          }
        }
        actionConfigs.value = next
      }
    }
  } catch {}
})

watch(
  notes,
  (next) => {
    if (notesSaveTimerId) window.clearTimeout(notesSaveTimerId)
    notesSaveTimerId = window.setTimeout(() => {
      try {
        window.localStorage.setItem('lanmountain.notes.v2', JSON.stringify({ notes: next }))
      } catch {}
    }, 250)
  },
  { deep: true }
)

const addNote = (): void => {
  const id = `${Date.now()}-${Math.random().toString(16).slice(2)}`
  notes.value = [{ id, text: '' }, ...notes.value]
}

watch(
  [widgetsOrder, widgetsEnabled],
  () => {
    if (widgetsSaveTimerId) window.clearTimeout(widgetsSaveTimerId)
    widgetsSaveTimerId = window.setTimeout(() => {
      try {
        window.localStorage.setItem(
          'lanmountain.desktop.widgets.v1',
          JSON.stringify({ order: widgetsOrder.value, enabled: widgetsEnabled.value })
        )
      } catch {}
    }, 250)
  },
  { deep: true }
)

watch(
  actionConfigs,
  (next) => {
    if (actionSaveTimerId) window.clearTimeout(actionSaveTimerId)
    actionSaveTimerId = window.setTimeout(() => {
      try {
        window.localStorage.setItem('lanmountain.desktop.actions.v1', JSON.stringify({ actions: next }))
      } catch {}
    }, 250)
  },
  { deep: true }
)

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
  mainPage.value = 'apps'
  if (apps.value.length === 0 || loadError.value) {
    await loadApps()
  }
}

const closeApps = (): void => {
  mainPage.value = 'home'
  query.value = ''
}

const openExternal = async (url: string): Promise<void> => {
  await window.api.call({ method: 'POST', path: '/open/external', body: { url } })
}

const activateAction = async (actionId: string, fallback?: () => void): Promise<void> => {
  const config = actionConfigs.value[actionId]
  const target = config?.target?.trim() ?? ''
  if (!config || !target) {
    fallback?.()
    return
  }
  if (config.kind === 'app') {
    await launch(target)
    return
  }
  await openExternal(target)
}

const openSettings = (): void => {
  isSettingsOpen.value = true
  settingsTab.value = 'homeEdit'
  isHomeEditMode.value = false
}

const closeSettings = (): void => {
  isSettingsOpen.value = false
  isHomeEditMode.value = false
}

const handleTilePointerUp = async (tile: DesktopTile): Promise<void> => {
  if (Date.now() < suppressTapUntil) return
  await tile.onActivate?.()
}

const handleWritingActionPointerUp = async (actionId: string): Promise<void> => {
  if (Date.now() < suppressTapUntil) return
  await activateAction(actionId)
}

const handlePagePointerDown = (event: PointerEvent): void => {
  if (isSettingsOpen.value) return
  if (pageSwipePointerId !== null) return
  const tag = (event.target as HTMLElement | null)?.tagName?.toLowerCase() ?? ''
  if (tag === 'input' || tag === 'textarea' || tag === 'select') return

  if (mainPage.value === 'apps' && event.clientX > 36) {
    return
  }

  pageSwipePointerId = event.pointerId
  pageSwipeStartX = event.clientX
  pageSwipeStartY = event.clientY
  pageSwipeStartTime = Date.now()
  pageSwipeStartMainPage = mainPage.value
}

const handlePagePointerMove = (event: PointerEvent): void => {
  if (pageSwipePointerId === null) return
  if (event.pointerId !== pageSwipePointerId) return

  const dx = event.clientX - pageSwipeStartX
  const dy = event.clientY - pageSwipeStartY
  if (Math.abs(dx) < 12 && Math.abs(dy) < 12) return

  if (Math.abs(dx) > Math.abs(dy) + 24 && Math.abs(dx) > 60) {
    suppressTapUntil = Date.now() + 350
  }
}

const handlePagePointerUp = (event: PointerEvent): void => {
  if (pageSwipePointerId === null) return
  if (event.pointerId !== pageSwipePointerId) return

  const dt = Date.now() - pageSwipeStartTime
  const dx = event.clientX - pageSwipeStartX
  const dy = event.clientY - pageSwipeStartY

  pageSwipePointerId = null

  if (dt > 900) return
  if (Math.abs(dx) < 90) return
  if (Math.abs(dx) <= Math.abs(dy) + 28) return

  suppressTapUntil = Date.now() + 450

  if (pageSwipeStartMainPage === 'home' && dx < -90) {
    void openApps()
    return
  }

  if (pageSwipeStartMainPage === 'apps' && dx > 90 && pageSwipeStartX <= 36) {
    closeApps()
  }
}

const handlePagePointerCancel = (event: PointerEvent): void => {
  if (pageSwipePointerId === null) return
  if (event.pointerId !== pageSwipePointerId) return
  pageSwipePointerId = null
}

const restoreDefaultWidgets = (): void => {
  widgetsOrder.value = [
    'writingPanel',
    'notesPanel',
    'whiteboard',
    'fileManager',
    'visualPresenter',
    'browser',
    'moreApps'
  ]
  widgetsEnabled.value = {
    writingPanel: true,
    notesPanel: true,
    whiteboard: true,
    fileManager: true,
    visualPresenter: true,
    browser: true,
    moreApps: true
  }
}

const toggleWidgetEnabled = (id: string): void => {
  const next = { ...widgetsEnabled.value }
  next[id] = !(next[id] !== false)
  widgetsEnabled.value = next
}

const setActionKind = (id: string, kind: ActionKind): void => {
  const current = actionConfigs.value[id] ?? { kind, target: '' }
  actionConfigs.value = { ...actionConfigs.value, [id]: { ...current, kind } }
}

const setActionTarget = (id: string, target: string): void => {
  const current = actionConfigs.value[id] ?? { kind: 'url', target: '' }
  actionConfigs.value = { ...actionConfigs.value, [id]: { ...current, target } }
}

const clearAction = (id: string): void => {
  const next = { ...actionConfigs.value }
  delete next[id]
  actionConfigs.value = next
}

onBeforeUnmount(() => {
  if (timerId) window.clearInterval(timerId)
  if (appsScrollTimerId) window.clearTimeout(appsScrollTimerId)
  if (launchTimerId) window.clearTimeout(launchTimerId)
  if (notesSaveTimerId) window.clearTimeout(notesSaveTimerId)
  if (widgetsSaveTimerId) window.clearTimeout(widgetsSaveTimerId)
  if (actionSaveTimerId) window.clearTimeout(actionSaveTimerId)
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
