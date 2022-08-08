import React, { FC, useEffect, useState } from 'react'
import {
  Group,
  Table,
  Text,
  ActionIcon,
  Badge,
  Avatar,
  TextInput,
  Paper,
  ScrollArea,
} from '@mantine/core'
import { useInputState } from '@mantine/hooks'
import { useModals } from '@mantine/modals'
import { showNotification } from '@mantine/notifications'
import {
  mdiArrowLeftBold,
  mdiArrowRightBold,
  mdiCheck,
  mdiMagnify,
  mdiClose,
  mdiDeleteOutline,
  mdiPencilOutline,
} from '@mdi/js'
import { Icon } from '@mdi/react'
import AdminPage from '@Components/admin/AdminPage'
import UserEditModal, { RoleColorMap } from '@Components/admin/UserEditModal'
import { showErrorNotification } from '@Utils/ApiErrorHandler'
import { useTableStyles } from '@Utils/ThemeOverride'
import api, { Role, UserInfoModel } from '@Api'

const ITEM_COUNT_PER_PAGE = 30

const Users: FC = () => {
  const [page, setPage] = useState(1)
  const [isEditModalOpen, setIsEditModalOpen] = useState(false)
  const [activeUser, setActiveUser] = useState<UserInfoModel>({})
  const [users, setUsers] = useState<UserInfoModel[]>()
  const [hint, setHint] = useInputState('')
  const [searching, setSearching] = useState(false)

  const { classes, theme } = useTableStyles()
  const { data: currentUser } = api.account.useAccountProfile({
    refreshInterval: 0,
    revalidateIfStale: false,
    revalidateOnFocus: false,
  })

  useEffect(() => {
    api.admin
      .adminUsers({
        count: ITEM_COUNT_PER_PAGE,
        skip: (page - 1) * ITEM_COUNT_PER_PAGE,
      })
      .then((res) => {
        setUsers(res.data)
      })
  }, [page])

  const onSearch = () => {
    if (!hint) {
      api.admin
        .adminUsers({
          count: ITEM_COUNT_PER_PAGE,
          skip: (page - 1) * ITEM_COUNT_PER_PAGE,
        })
        .then((res) => {
          setUsers(res.data)
        })
      return
    }

    setSearching(true)

    api.admin
      .adminSearchUsers({
        hint,
      })
      .then((res) => {
        setUsers(res.data)
      })
      .catch(showErrorNotification)
      .finally(() => {
        setSearching(false)
      })
  }

  const modals = useModals()

  const onConfirmDelete = (user: UserInfoModel) => {
    api.admin
      .adminDeleteUser(user.id!)
      .then(() => {
        showNotification({
          color: 'teal',
          message: '用户已删除',
          icon: <Icon path={mdiCheck} size={1} />,
          disallowClose: true,
        })
        setUsers(users?.filter((t) => t.id !== user.id) ?? [])
      })
      .catch(showErrorNotification)
  }

  const onDeleteUser = (user: UserInfoModel) => {
    if (!user) {
      return
    }

    if (user.id === currentUser?.userId) {
      showNotification({
        color: 'orange',
        message: '不可以删除自己',
        icon: <Icon path={mdiClose} size={1} />,
      })
      return
    }

    modals.openConfirmModal({
      title: '删除用户',
      children: <Text size="sm">你确定要删除用户 "{user.userName}" 吗？</Text>,
      onConfirm: () => onConfirmDelete(user),
      centered: true,
      labels: { confirm: '删除用户', cancel: '取消' },
      confirmProps: { color: 'red' },
    })
  }

  return (
    <AdminPage
      isLoading={searching || !users}
      head={
        <>
          <TextInput
            icon={<Icon path={mdiMagnify} size={1} />}
            style={{ width: '30%' }}
            placeholder="搜索用户名/邮箱/学号/姓名"
            value={hint}
            onChange={setHint}
            onKeyDown={(e) => {
              !searching && e.key === 'Enter' && onSearch()
            }}
          />
          <Group position="right">
            <ActionIcon size="lg" disabled={page <= 1} onClick={() => setPage(page - 1)}>
              <Icon path={mdiArrowLeftBold} size={1} />
            </ActionIcon>
            <ActionIcon
              size="lg"
              disabled={users && users.length < ITEM_COUNT_PER_PAGE}
              onClick={() => setPage(page + 1)}
            >
              <Icon path={mdiArrowRightBold} size={1} />
            </ActionIcon>
          </Group>
        </>
      }
    >
      <Paper shadow="md" p="xs">
        <ScrollArea offsetScrollbars scrollbarSize={4} style={{ height: 'calc(100vh - 190px)' }}>
          <Table className={classes.table}>
            <thead>
              <tr>
                <th>用户</th>
                <th>邮箱</th>
                <th>最后一次访问</th>
                <th>真实姓名</th>
                <th>学号</th>
                <th>操作</th>
              </tr>
            </thead>
            <tbody>
              {users &&
                users.map((user) => (
                  <tr key={user.id}>
                    <td>
                      <Group position="apart">
                        <Group position="left">
                          <Avatar src={user.avatar} radius="xl" />
                          <Text>{user.userName}</Text>
                        </Group>
                        <Badge size="sm" color={RoleColorMap.get(user.role ?? Role.User)}>
                          {user.role}
                        </Badge>
                      </Group>
                    </td>
                    <td>
                      <Text size="sm" style={{ fontFamily: theme.fontFamilyMonospace }}>
                        {user.email}
                      </Text>
                    </td>
                    <td>
                      <Group noWrap position="apart">
                        <Text
                          lineClamp={1}
                          size="sm"
                          style={{ fontFamily: theme.fontFamilyMonospace }}
                        >
                          {user.ip}
                        </Text>
                        <Badge size="xs" color="cyan" variant="outline">
                          {new Date(user.lastVisitedUTC!).toLocaleString()}
                        </Badge>
                      </Group>
                    </td>
                    <td>{!user.realName ? '用户未填写' : user.realName}</td>
                    <td>
                      <Text size="sm" style={{ fontFamily: theme.fontFamilyMonospace }}>
                        {!user.stdNumber ? '00000000' : user.stdNumber}
                      </Text>
                    </td>
                    <td>
                      <Group>
                        <ActionIcon
                          onClick={() => {
                            setActiveUser(user)
                            setIsEditModalOpen(true)
                          }}
                        >
                          <Icon path={mdiPencilOutline} size={1} />
                        </ActionIcon>
                        <ActionIcon
                          disabled={user.id === currentUser?.userId}
                          onClick={() => onDeleteUser(user)}
                          color="alert"
                        >
                          <Icon path={mdiDeleteOutline} size={1} />
                        </ActionIcon>
                      </Group>
                    </td>
                  </tr>
                ))}
            </tbody>
          </Table>
        </ScrollArea>
        <UserEditModal
          centered
          size="30%"
          title="编辑用户"
          user={activeUser}
          opened={isEditModalOpen}
          onClose={() => setIsEditModalOpen(false)}
          mutateUser={(user: UserInfoModel) => {
            setUsers(
              [user, ...(users?.filter((n) => n.id !== user.id) ?? [])].sort((a, b) =>
                a.id! < b.id! ? -1 : 1
              )
            )
          }}
        />
      </Paper>
    </AdminPage>
  )
}

export default Users
